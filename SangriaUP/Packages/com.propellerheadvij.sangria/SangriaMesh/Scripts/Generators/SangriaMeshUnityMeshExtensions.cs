using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SangriaMesh
{
    public static class SangriaMeshUnityMeshExtensions
    {
        private struct AttributeMap
        {
            public int SangriaId;
            public VertexAttribute UnityAttr;
            public VertexAttributeFormat Format;
            public int Dimension;
        }

        private static readonly AttributeMap[] SupportedAttributes =
        {
            new() { SangriaId = AttributeID.Position, UnityAttr = VertexAttribute.Position, Format = VertexAttributeFormat.Float32, Dimension = 3 },
            new() { SangriaId = AttributeID.Normal, UnityAttr = VertexAttribute.Normal, Format = VertexAttributeFormat.Float32, Dimension = 3 },
            new() { SangriaId = AttributeID.Tangent, UnityAttr = VertexAttribute.Tangent, Format = VertexAttributeFormat.Float32, Dimension = 4 },
            new() { SangriaId = AttributeID.Color, UnityAttr = VertexAttribute.Color, Format = VertexAttributeFormat.Float32, Dimension = 4 },
            new() { SangriaId = AttributeID.UV0, UnityAttr = VertexAttribute.TexCoord0, Format = VertexAttributeFormat.Float32, Dimension = 2 },
            new() { SangriaId = AttributeID.UV1, UnityAttr = VertexAttribute.TexCoord1, Format = VertexAttributeFormat.Float32, Dimension = 2 },
            new() { SangriaId = AttributeID.UV2, UnityAttr = VertexAttribute.TexCoord2, Format = VertexAttributeFormat.Float32, Dimension = 2 },
            new() { SangriaId = AttributeID.UV3, UnityAttr = VertexAttribute.TexCoord3, Format = VertexAttributeFormat.Float32, Dimension = 2 },
            new() { SangriaId = AttributeID.UV4, UnityAttr = VertexAttribute.TexCoord4, Format = VertexAttributeFormat.Float32, Dimension = 2 },
            new() { SangriaId = AttributeID.UV5, UnityAttr = VertexAttribute.TexCoord5, Format = VertexAttributeFormat.Float32, Dimension = 2 },
            new() { SangriaId = AttributeID.UV6, UnityAttr = VertexAttribute.TexCoord6, Format = VertexAttributeFormat.Float32, Dimension = 2 },
            new() { SangriaId = AttributeID.UV7, UnityAttr = VertexAttribute.TexCoord7, Format = VertexAttributeFormat.Float32, Dimension = 2 },
        };

        private unsafe struct ActiveAttribute
        {
            public AttributeMap Map;
            public MeshDomain Domain;
            public CompiledAttributeRawAccessor Raw;
            public void* UnityDstPtr;
        }

        private const MeshUpdateFlags TriangleSetSubMeshFlags =
            MeshUpdateFlags.DontRecalculateBounds |
            MeshUpdateFlags.DontValidateIndices;

        private const MeshUpdateFlags TriangleApplyFlags =
            TriangleSetSubMeshFlags |
            MeshUpdateFlags.DontNotifyMeshUsers;

        /// <summary>
        /// Converts editable SangriaMesh detail to Unity Mesh.
        /// Supports polygon primitives and triangulates them with ear clipping (fan fallback).
        /// </summary>
        public static void FillUnityMesh(this ref NativeDetail detail, Mesh mesh, Allocator allocator = Allocator.TempJob)
        {
            NativeCompiledDetail compiled = detail.Compile(allocator);
            try
            {
                FillUnityMesh(compiled, mesh);
            }
            finally
            {
                compiled.Dispose();
            }
        }

        /// <summary>
        /// Converts compiled SangriaMesh detail to Unity Mesh.
        /// Supports polygon primitives and triangulates them with ear clipping (fan fallback).
        /// </summary>
        public static void FillUnityMesh(this in NativeCompiledDetail compiled, Mesh mesh)
        {
            FillUnityMeshInternal(compiled, mesh, assumeTriangleTopology: false);
        }

        /// <summary>
        /// Fast path for known triangle-only topology.
        /// </summary>
        public static void FillUnityMeshTriangles(this in NativeCompiledDetail compiled, Mesh mesh)
        {
            FillUnityMeshTrianglesMeshData(compiled, mesh);
        }

        private static unsafe void FillUnityMeshTrianglesMeshData(in NativeCompiledDetail compiled, Mesh mesh)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            mesh.Clear();

            if (compiled.VertexCount <= 0 || compiled.PrimitiveCount <= 0)
                return;
            if (!IsTriangleOnlyTopology(compiled))
                throw new InvalidOperationException("FillUnityMeshTriangles requires triangle-only topology. Use FillUnityMesh for polygon primitives.");

            var primitiveVerticesDense = compiled.GetPrimitiveVerticesDenseArrayUnsafe();
            int* triangleIndicesPtr = (int*)primitiveVerticesDense.GetUnsafeReadOnlyPtr();
            ApplyTriangleMeshData(
                compiled,
                mesh,
                triangleIndicesPtr,
                primitiveVerticesDense.Length);
        }

        private static unsafe void FillUnityMeshInternal(in NativeCompiledDetail compiled, Mesh mesh, bool assumeTriangleTopology)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            if (assumeTriangleTopology || IsTriangleOnlyTopology(compiled))
            {
                FillUnityMeshTrianglesMeshData(compiled, mesh);
                return;
            }

            mesh.Clear();

            if (compiled.VertexCount <= 0 || compiled.PrimitiveCount <= 0)
                return;

            if (compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var pointPositions) != CoreResult.Success)
                throw new InvalidOperationException("Point position attribute is required for mesh conversion.");

            int pointCount = pointPositions.Length;

            int triangleIndexCount = 0;
            for (int primitiveIndex = 0; primitiveIndex < compiled.PrimitiveCount; primitiveIndex++)
            {
                int start = compiled.PrimitiveOffsetsDense[primitiveIndex];
                int end = compiled.PrimitiveOffsetsDense[primitiveIndex + 1];
                int primitiveVertexCount = end - start;

                if (primitiveVertexCount >= 3)
                    triangleIndexCount += (primitiveVertexCount - 2) * 3;
            }

            var triangles = new NativeArray<int>(triangleIndexCount, Allocator.Temp);
            int triangleWriteIndex = 0;

            var primitiveVertices = new NativeList<int>(16, Allocator.Temp);
            var primitivePositions = new NativeList<float3>(16, Allocator.Temp);
            var projectedPolygon = new NativeList<float2>(16, Allocator.Temp);
            var polygonOrder = new NativeList<int>(16, Allocator.Temp);

            try
            {
                for (int primitiveIndex = 0; primitiveIndex < compiled.PrimitiveCount; primitiveIndex++)
                {
                    int start = compiled.PrimitiveOffsetsDense[primitiveIndex];
                    int end = compiled.PrimitiveOffsetsDense[primitiveIndex + 1];
                    int primitiveVertexCount = end - start;

                    if (primitiveVertexCount < 3)
                        continue;

                    if (primitiveVertexCount == 3)
                    {
                        triangles[triangleWriteIndex++] = compiled.PrimitiveVerticesDense[start];
                        triangles[triangleWriteIndex++] = compiled.PrimitiveVerticesDense[start + 1];
                        triangles[triangleWriteIndex++] = compiled.PrimitiveVerticesDense[start + 2];
                        continue;
                    }

                    primitiveVertices.Clear();
                    primitivePositions.Clear();
                    int primitiveStartWriteIndex = triangleWriteIndex;

                    bool hasInvalidPoint = false;
                    for (int i = 0; i < primitiveVertexCount; i++)
                    {
                        int vertexIndex = compiled.PrimitiveVerticesDense[start + i];
                        primitiveVertices.Add(vertexIndex);

                        int pointIndex = compiled.VertexToPointDense[vertexIndex];
                        if ((uint)pointIndex < (uint)pointCount)
                        {
                            primitivePositions.Add(pointPositions[pointIndex]);
                        }
                        else
                        {
                            hasInvalidPoint = true;
                            primitivePositions.Add(default);
                        }
                    }

                    if (hasInvalidPoint || !SangriaMeshGeometry.TryBuildProjectedPolygon(primitivePositions, projectedPolygon))
                    {
                        triangleWriteIndex = SangriaMeshGeometry.WriteFanTriangulation(primitiveVertices, triangles, primitiveStartWriteIndex);
                        SangriaMeshGeometry.ValidatePrimitiveTriangulationWrite(
                            primitiveVertexCount,
                            primitiveStartWriteIndex,
                            triangleWriteIndex);
                        continue;
                    }

                    if (!SangriaMeshGeometry.TryTriangulateEarClip(primitiveVertices, projectedPolygon, polygonOrder, triangles, ref triangleWriteIndex))
                        triangleWriteIndex = SangriaMeshGeometry.WriteFanTriangulation(primitiveVertices, triangles, primitiveStartWriteIndex);

                    SangriaMeshGeometry.ValidatePrimitiveTriangulationWrite(
                        primitiveVertexCount,
                        primitiveStartWriteIndex,
                        triangleWriteIndex);
                }

                ApplyTriangleMeshData(
                    compiled,
                    mesh,
                    (int*)triangles.GetUnsafeReadOnlyPtr(),
                    triangles.Length);
            }
            finally
            {
                triangles.Dispose();
                primitiveVertices.Dispose();
                primitivePositions.Dispose();
                projectedPolygon.Dispose();
                polygonOrder.Dispose();
            }
        }

        private static unsafe void ApplyTriangleMeshData(
            in NativeCompiledDetail compiled,
            Mesh mesh,
            int* triangleIndicesPtr,
            int indexCount)
        {
            var activeAttributes = CollectActiveAttributes(compiled);
            if (activeAttributes.Count == 0)
                throw new InvalidOperationException("No supported attributes found for mesh conversion. Position is required.");

            int vertexCount = compiled.VertexCount;
            IndexFormat indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            var layout = new VertexAttributeDescriptor[activeAttributes.Count];
            for (int i = 0; i < activeAttributes.Count; i++)
            {
                var attr = activeAttributes[i];
                layout[i] = new VertexAttributeDescriptor(attr.Map.UnityAttr, attr.Map.Format, attr.Map.Dimension, stream: i);
            }

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            bool meshDataApplied = false;
            try
            {
                var meshData = meshDataArray[0];
                meshData.SetVertexBufferParams(vertexCount, layout);
                meshData.SetIndexBufferParams(indexCount, indexFormat);

                bool hasNormals = false;
                for (int i = 0; i < activeAttributes.Count; i++)
                {
                    var attr = activeAttributes[i];
                    attr.UnityDstPtr = meshData.GetVertexData<byte>(i).GetUnsafePtr();
                    activeAttributes[i] = attr;
                    if (attr.Map.UnityAttr == VertexAttribute.Normal)
                        hasNormals = true;
                }

                var vertexToPointDense = compiled.GetVertexToPointDenseArrayUnsafe();
                int* vertexToPointPtr = (int*)vertexToPointDense.GetUnsafeReadOnlyPtr();

                float3 minPos = new float3(float.MaxValue);
                float3 maxPos = new float3(float.MinValue);
                bool hasBounds = false;

                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    int pointIndex = vertexToPointPtr[vertexIndex];

                    foreach (var attr in activeAttributes)
                    {
                        int srcIndex = attr.Domain == MeshDomain.Vertex ? vertexIndex : pointIndex;
                        void* src = attr.Raw.GetPointerUnchecked(srcIndex);
                        void* dst = (byte*)attr.UnityDstPtr + (long)vertexIndex * attr.Raw.Stride;
                        UnsafeUtility.MemCpy(dst, src, attr.Raw.Stride);

                        if (attr.Map.UnityAttr == VertexAttribute.Position)
                        {
                            float3 pos = *(float3*)src;
                            minPos = math.min(minPos, pos);
                            maxPos = math.max(maxPos, pos);
                            hasBounds = true;
                        }
                    }
                }

                if (indexFormat == IndexFormat.UInt32)
                {
                    var indexData = meshData.GetIndexData<int>();
                    UnsafeUtility.MemCpy(
                        indexData.GetUnsafePtr(),
                        triangleIndicesPtr,
                        (long)indexCount * UnsafeUtility.SizeOf<int>());
                }
                else
                {
                    var indexData = meshData.GetIndexData<ushort>();
                    ushort* indexDst = (ushort*)indexData.GetUnsafePtr();
                    for (int i = 0; i < indexCount; i++)
                        indexDst[i] = (ushort)triangleIndicesPtr[i];
                }

                meshData.subMeshCount = 1;
                var subMeshDescriptor = new SubMeshDescriptor(0, indexCount)
                {
                    bounds = hasBounds
                        ? new Bounds((minPos + maxPos) * 0.5f, maxPos - minPos)
                        : new Bounds(Vector3.zero, Vector3.zero)
                };

                meshData.SetSubMesh(0, subMeshDescriptor, TriangleSetSubMeshFlags);
                Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh, TriangleApplyFlags);
                meshDataApplied = true;
                mesh.bounds = subMeshDescriptor.bounds;
                if (!hasNormals)
                    mesh.RecalculateNormals();
            }
            catch
            {
                if (!meshDataApplied)
                    meshDataArray.Dispose();
                throw;
            }
        }

        private static List<ActiveAttribute> CollectActiveAttributes(in NativeCompiledDetail compiled)
        {
            var active = new List<ActiveAttribute>();
            foreach (var map in SupportedAttributes)
            {
                if (compiled.TryGetRawAttributeAccessor(MeshDomain.Vertex, map.SangriaId, out var vRaw) == CoreResult.Success)
                {
                    ValidateStride(map, vRaw);
                    active.Add(new ActiveAttribute { Map = map, Domain = MeshDomain.Vertex, Raw = vRaw });
                }
                else if (compiled.TryGetRawAttributeAccessor(MeshDomain.Point, map.SangriaId, out var pRaw) == CoreResult.Success)
                {
                    ValidateStride(map, pRaw);
                    active.Add(new ActiveAttribute { Map = map, Domain = MeshDomain.Point, Raw = pRaw });
                }
            }

            return active;
        }

        private static void ValidateStride(AttributeMap map, CompiledAttributeRawAccessor raw)
        {
            int expectedStride = GetVertexAttributeFormatSize(map.Format) * map.Dimension;
            if (raw.Stride != expectedStride)
            {
                throw new InvalidOperationException(
                    $"Stride mismatch for attribute {map.UnityAttr}. " +
                    $"Expected {expectedStride} (Format: {map.Format}, Dim: {map.Dimension}), " +
                    $"but Sangria provided {raw.Stride}. " +
                    "TODO: Implement slow path for format conversion.");
            }
        }

        private static int GetVertexAttributeFormatSize(VertexAttributeFormat format)
        {
            return format switch
            {
                VertexAttributeFormat.Float32 => 4,
                VertexAttributeFormat.Float16 => 2,
                VertexAttributeFormat.UNorm8 => 1,
                VertexAttributeFormat.SNorm8 => 1,
                VertexAttributeFormat.UNorm16 => 2,
                VertexAttributeFormat.SNorm16 => 2,
                VertexAttributeFormat.UInt8 => 1,
                VertexAttributeFormat.SInt8 => 1,
                VertexAttributeFormat.UInt16 => 2,
                VertexAttributeFormat.SInt16 => 2,
                VertexAttributeFormat.UInt32 => 4,
                VertexAttributeFormat.SInt32 => 4,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
        }


        private static bool IsTriangleOnlyTopology(in NativeCompiledDetail compiled)
        {
            return compiled.IsTriangleOnlyTopology;
        }

        /// <summary>
        /// Creates a new Unity Mesh from editable SangriaMesh detail.
        /// </summary>
        public static Mesh ToUnityMesh(this ref NativeDetail detail, string meshName = "SangriaMesh", Allocator allocator = Allocator.TempJob)
        {
            var mesh = new Mesh { name = meshName };
            try
            {
                detail.FillUnityMesh(mesh, allocator);
                return mesh;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }

        /// <summary>
        /// Creates a new Unity Mesh from compiled SangriaMesh detail.
        /// </summary>
        public static Mesh ToUnityMesh(this in NativeCompiledDetail compiled, string meshName = "SangriaMesh")
        {
            var mesh = new Mesh { name = meshName };
            try
            {
                compiled.FillUnityMesh(mesh);
                return mesh;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }

        /// <summary>
        /// Creates a new Unity Mesh using a fast triangle-only path.
        /// </summary>
        public static Mesh ToUnityMeshTriangles(this in NativeCompiledDetail compiled, string meshName = "SangriaMesh")
        {
            var mesh = new Mesh { name = meshName };
            try
            {
                compiled.FillUnityMeshTriangles(mesh);
                return mesh;
            }
            catch
            {
                UnityEngine.Object.DestroyImmediate(mesh);
                throw;
            }
        }
    }
}
