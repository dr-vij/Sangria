using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEngine.Mesh;

namespace ViJMeshTools
{
    public static partial class MeshCutter
    {
        /// <summary>
        /// Cuts MeshData with plane and tesselates the cut contour
        /// </summary>
        /// <param name="meshData"></param>
        /// <param name="planeTransform"></param>
        /// <param name="meshTransform"></param>
        /// <param name="positiveMesh"></param>
        /// <param name="negativeMesh"></param>
        public static void CutMeshWithTesselation(MeshData meshData, Transform planeTransform, Transform meshTransform, Mesh positiveMesh, Mesh negativeMesh)
        {
            #region Initial cut job
            var worldPlane = new Plane(planeTransform.up, planeTransform.position);
            var cutterJob = new CutMeshWithPlaneJob(meshData, worldPlane, meshTransform);
            var cutterJobHandle = cutterJob.Schedule();
            cutterJobHandle.Complete();
            #endregion

            #region Tesselating the cut part of the plane
            var tesselationResult = TessAdapter.CreateContours(cutterJob.CutSegments, planeTransform.forward, planeTransform.right, worldPlane.normal);
            var edgeCutVertices = new NativeArray<VertexBufferData>(tesselationResult.Vertices.Length, Allocator.Persistent);
            var edgeCutIndicies = tesselationResult.Indicies;
            var edgeCutVertexCount = edgeCutVertices.Length;

            for (int i = 0; i < edgeCutVertexCount; i++)
            {
                edgeCutVertices[i] = new VertexBufferData()
                {
                    Position = tesselationResult.Vertices[i],
                    Normal = tesselationResult.Normal,
                    Tangent = new float4(),
                    TexCoord0 = tesselationResult.UVs[i],
                };
            }
            #endregion

            #region Combining tess result with meshes on both sides of the plane cutter
            var writableMeshData = AllocateWritableMeshData(2);
            CombineMeshWithTesselation(writableMeshData[0], cutterJob.PositiveIndicies, cutterJob.PositiveVertices, cutterJob.PositiveSubmeshes, edgeCutIndicies, edgeCutVertices, true);

            //Flipping the triangles:
            for (int i = 0; i < edgeCutIndicies.Length; i += 3)
            {
                var buf = edgeCutIndicies[i];
                edgeCutIndicies[i] = edgeCutIndicies[i + 2];
                edgeCutIndicies[i + 2] = buf;
            }
            CombineMeshWithTesselation(writableMeshData[1], cutterJob.NegativeIndicies, cutterJob.NegativeVertices, cutterJob.NegativeSubmeshes, edgeCutIndicies, edgeCutVertices, false);
            #endregion

            ApplyAndDisposeWritableMeshData(writableMeshData, new[] { positiveMesh, negativeMesh }, flags: MeshUpdateFlags.DontRecalculateBounds);
            positiveMesh.RecalculateBounds();
            negativeMesh.RecalculateBounds();
            cutterJob.Dispose();
            tesselationResult.Dispose();
            edgeCutVertices.Dispose();
        }

        /// <summary>
        /// This method combines tess result with MeshData
        /// </summary>
        /// <param name="meshData">Writable mesh data</param>
        /// <param name="initialIndicies">All indicies of the mesh we want to write. IT WILL BE MODIFIYED</param>
        /// <param name="initialVertices">All vertices of the mesh we want to write. IT WILL BE MODIFYIED</param>
        /// <param name="initialSubmeshes">Submeshes that we have in vertices and indicies arrays</param>
        /// <param name="tessIndicies">Cut contour indicies</param>
        /// <param name="tessVertices">Cut contour vertices</param>
        /// <param name="tessSubmesh">the submesh that should be applied to cut contour</param>
        public static void CombineMeshWithTesselation(
            MeshData meshData, NativeList<int> initialIndicies, NativeList<VertexBufferData> initialVertices, NativeArray<SubMeshDescriptor> initialSubmeshes,
            NativeArray<int> tessIndicies, NativeArray<VertexBufferData> tessVertices,
            bool addNewSubmesh)
        {
            var initialVertexCount = initialVertices.Length;
            var initialIndexCount = initialIndicies.Length;

            //Preparing write index and vertex data
            meshData.SetIndexBufferParams(initialIndicies.Length + tessIndicies.Length, IndexFormat.UInt32);
            meshData.SetVertexBufferParams(initialVertices.Length + tessVertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, dimension: 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, dimension: 3, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, dimension: 4, stream: 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, dimension: 2, stream: 0)
                );

            var submeshes = new NativeList<SubMeshDescriptor>(Allocator.Persistent);
            foreach (var submesh in initialSubmeshes)
                submeshes.Add(submesh);

            if (addNewSubmesh)
            {
                initialIndicies.AddRange(tessIndicies);
                initialVertices.AddRange(tessVertices);

                var lastSubmesh = new SubMeshDescriptor();
                lastSubmesh.indexStart = initialIndexCount;
                lastSubmesh.indexCount = tessIndicies.Length;
                lastSubmesh.firstVertex = initialVertexCount;
                lastSubmesh.vertexCount = tessVertices.Length;
                lastSubmesh.baseVertex = initialIndexCount;
                submeshes.Add(lastSubmesh);
            }
            else
            {
                var lastSubmesh = submeshes[submeshes.Length - 1];
                int addIndex = (int)lastSubmesh.vertexCount;
                for (int i = 0; i < tessIndicies.Length; i++)
                    initialIndicies.Add((int)(tessIndicies[i] + addIndex));
                initialVertices.AddRange(tessVertices);
                lastSubmesh.vertexCount += tessVertices.Length;
                lastSubmesh.indexCount += tessIndicies.Length;
                submeshes[submeshes.Length - 1] = lastSubmesh;
            }

            var meshVertices = meshData.GetVertexData<VertexBufferData>(0);
            var meshIndicies = meshData.GetIndexData<int>();
            initialVertices.AsArray().CopyTo(meshVertices);
            initialIndicies.AsArray().CopyTo(meshIndicies);

            //Setting submeshes
            meshData.subMeshCount = submeshes.Length;
            for (int i = 0; i < submeshes.Length; i++)
                meshData.SetSubMesh(i, submeshes[i], MeshUpdateFlags.DontRecalculateBounds);
            submeshes.Dispose();
        }
    }
}