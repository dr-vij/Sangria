using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

namespace PropellerheadMesh
{
    public static class NativeDetailMeshExtensions
    {
        public static void FillUnityMesh(this NativeDetail nativeDetail, Mesh mesh, Allocator allocator = Allocator.Temp)
        {
            mesh.Clear();

            var allVertices = new List<Vector3>();
            var allNormals = new List<Vector3>();
            var allTriangles = new List<int>();

            // Get accessors for position and normal attributes
            nativeDetail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);
            bool hasVertexNormals = nativeDetail.GetVertexAttributeAccessor<float3>(AttributeID.Normal, out var vertexNormalAccessor) == AttributeMapResult.Success;

            // Get all valid primitives
            var validPrimitives = new NativeList<int>(allocator);
            nativeDetail.GetAllValidPrimitives(validPrimitives);

            for (int i = 0; i < validPrimitives.Length; i++)
            {
                int primIndex = validPrimitives[i];
                
                // Get primitive vertex indices directly as slice
                var primVertices = nativeDetail.GetPrimitiveVertices(primIndex);
                if (primVertices.Length < 3)
                    continue;

                // Get the actual vertex positions and normals
                var vertexPositions = new List<Vector3>();
                var vertexNormals = new List<Vector3>();
                
                for (int v = 0; v < primVertices.Length; v++)
                {
                    int vertexIndex = primVertices[v];
                    int pointIndex = nativeDetail.GetVertexPointUnsafe(vertexIndex);
                    if (pointIndex >= 0)
                    {
                        vertexPositions.Add(positionAccessor[pointIndex]);
                        
                        if (hasVertexNormals)
                        {
                            vertexNormals.Add(vertexNormalAccessor[vertexIndex]);
                        }
                        else
                        {
                            vertexNormals.Add(Vector3.zero);
                        }
                    }
                }

                // Add vertices to mesh
                int baseIndex = allVertices.Count;
                allVertices.AddRange(vertexPositions);
                allNormals.AddRange(vertexNormals);

                // Triangulate the primitive (fan triangulation)
                for (int t = 1; t < vertexPositions.Count - 1; t++)
                {
                    allTriangles.Add(baseIndex);
                    allTriangles.Add(baseIndex + t);
                    allTriangles.Add(baseIndex + t + 1);
                }
            }

            validPrimitives.Dispose();

            if (allVertices.Count == 0)
                return;

            mesh.vertices = allVertices.ToArray();
            mesh.normals = allNormals.ToArray();
            mesh.triangles = allTriangles.ToArray();
            mesh.RecalculateBounds();
        }
    }
}