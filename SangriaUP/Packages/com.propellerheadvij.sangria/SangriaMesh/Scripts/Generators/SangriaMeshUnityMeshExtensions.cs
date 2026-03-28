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
        private const float EarClipEpsilon = 1e-6f;

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

                    if (hasInvalidPoint || !TryBuildProjectedPolygon(primitivePositions, projectedPolygon))
                    {
                        triangleWriteIndex = WriteFanTriangulation(primitiveVertices, triangles, primitiveStartWriteIndex);
                        ValidatePrimitiveTriangulationWrite(
                            primitiveVertexCount,
                            primitiveStartWriteIndex,
                            triangleWriteIndex);
                        continue;
                    }

                    if (!TryTriangulateEarClip(primitiveVertices, projectedPolygon, polygonOrder, triangles, ref triangleWriteIndex))
                        triangleWriteIndex = WriteFanTriangulation(primitiveVertices, triangles, primitiveStartWriteIndex);

                    ValidatePrimitiveTriangulationWrite(
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

                float minX = 0f, minY = 0f, minZ = 0f;
                float maxX = 0f, maxY = 0f, maxZ = 0f;
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
                            if (!hasBounds)
                            {
                                minX = maxX = pos.x;
                                minY = maxY = pos.y;
                                minZ = maxZ = pos.z;
                                hasBounds = true;
                            }
                            else
                            {
                                if (pos.x < minX) minX = pos.x;
                                else if (pos.x > maxX) maxX = pos.x;

                                if (pos.y < minY) minY = pos.y;
                                else if (pos.y > maxY) maxY = pos.y;

                                if (pos.z < minZ) minZ = pos.z;
                                else if (pos.z > maxZ) maxZ = pos.z;
                            }
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
                        ? new Bounds(
                            new Vector3(
                                (minX + maxX) * 0.5f,
                                (minY + maxY) * 0.5f,
                                (minZ + maxZ) * 0.5f),
                            new Vector3(
                                maxX - minX,
                                maxY - minY,
                                maxZ - minZ))
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

        private static bool TryBuildProjectedPolygon(NativeList<float3> positions, NativeList<float2> projectedPolygon)
        {
            projectedPolygon.Clear();
            int count = positions.Length;
            if (count < 3)
                return false;

            float3 normal = default;
            for (int i = 0; i < count; i++)
            {
                float3 current = positions[i];
                float3 next = positions[(i + 1) % count];

                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            float3 absNormal = math.abs(normal);
            if (absNormal is { x: <= EarClipEpsilon, y: <= EarClipEpsilon, z: <= EarClipEpsilon })
                return false;

            int dropAxis;
            if (absNormal.x >= absNormal.y && absNormal.x >= absNormal.z)
                dropAxis = 0;
            else if (absNormal.y >= absNormal.x && absNormal.y >= absNormal.z)
                dropAxis = 1;
            else
                dropAxis = 2;

            for (int i = 0; i < count; i++)
            {
                float3 p = positions[i];
                if (dropAxis == 0)
                    projectedPolygon.Add(new float2(p.y, p.z));
                else if (dropAxis == 1)
                    projectedPolygon.Add(new float2(p.x, p.z));
                else
                    projectedPolygon.Add(new float2(p.x, p.y));
            }

            return true;
        }

        private static bool TryTriangulateEarClip(
            NativeList<int> primitiveVertices,
            NativeList<float2> projectedPolygon,
            NativeList<int> polygonOrder,
            NativeArray<int> triangles,
            ref int triangleWriteIndex)
        {
            polygonOrder.Clear();
            int vertexCount = primitiveVertices.Length;
            for (int i = 0; i < vertexCount; i++)
                polygonOrder.Add(i);

            float area2 = ComputeSignedArea2(projectedPolygon, polygonOrder);
            if (math.abs(area2) <= EarClipEpsilon)
                return false;

            bool isCcw = area2 > 0f;
            int guard = 0;
            int guardLimit = vertexCount * vertexCount;

            while (polygonOrder.Length > 3 && guard++ < guardLimit)
            {
                bool earFound = false;
                int count = polygonOrder.Length;

                for (int i = 0; i < count; i++)
                {
                    int prevOrder = (i - 1 + count) % count;
                    int nextOrder = (i + 1) % count;

                    int prev = polygonOrder[prevOrder];
                    int curr = polygonOrder[i];
                    int next = polygonOrder[nextOrder];

                    float2 a = projectedPolygon[prev];
                    float2 b = projectedPolygon[curr];
                    float2 c = projectedPolygon[next];

                    float cross = Cross2(b - a, c - b);
                    if (isCcw ? cross <= EarClipEpsilon : cross >= -EarClipEpsilon)
                        continue;

                    bool containsOtherPoint = false;
                    for (int j = 0; j < count; j++)
                    {
                        if (j == prevOrder || j == i || j == nextOrder)
                            continue;

                        int test = polygonOrder[j];
                        if (PointInTriangleInclusive(projectedPolygon[test], a, b, c))
                        {
                            containsOtherPoint = true;
                            break;
                        }
                    }

                    if (containsOtherPoint)
                        continue;

                    triangles[triangleWriteIndex++] = primitiveVertices[prev];
                    triangles[triangleWriteIndex++] = primitiveVertices[curr];
                    triangles[triangleWriteIndex++] = primitiveVertices[next];

                    polygonOrder.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    return false;
            }

            if (polygonOrder.Length != 3)
                return false;

            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[0]];
            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[1]];
            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[2]];
            return true;
        }

        private static int WriteFanTriangulation(NativeList<int> primitiveVertices, NativeArray<int> triangles, int triangleWriteIndex)
        {
            if (primitiveVertices.Length < 3)
                return triangleWriteIndex;

            int root = primitiveVertices[0];
            for (int i = 1; i < primitiveVertices.Length - 1; i++)
            {
                triangles[triangleWriteIndex++] = root;
                triangles[triangleWriteIndex++] = primitiveVertices[i];
                triangles[triangleWriteIndex++] = primitiveVertices[i + 1];
            }

            return triangleWriteIndex;
        }

        [Conditional("UNITY_ASSERTIONS")]
        [Conditional("DEVELOPMENT_BUILD")]
        private static void ValidatePrimitiveTriangulationWrite(
            int primitiveVertexCount,
            int primitiveStartWriteIndex,
            int primitiveEndWriteIndex)
        {
            int expectedIndices = (primitiveVertexCount - 2) * 3;
            int writtenIndices = primitiveEndWriteIndex - primitiveStartWriteIndex;
            if (writtenIndices != expectedIndices)
            {
                throw new InvalidOperationException(
                    $"Invalid triangulation write count. Expected {expectedIndices}, wrote {writtenIndices}.");
            }
        }

        private static float ComputeSignedArea2(NativeList<float2> points, NativeList<int> order)
        {
            float sum = 0f;
            int count = order.Length;

            for (int i = 0; i < count; i++)
            {
                float2 a = points[order[i]];
                float2 b = points[order[(i + 1) % count]];
                sum += a.x * b.y - b.x * a.y;
            }

            return sum;
        }

        private static bool PointInTriangleInclusive(float2 p, float2 a, float2 b, float2 c)
        {
            float c0 = Cross2(b - a, p - a);
            float c1 = Cross2(c - b, p - b);
            float c2 = Cross2(a - c, p - c);

            bool hasPositive = c0 > EarClipEpsilon || c1 > EarClipEpsilon || c2 > EarClipEpsilon;
            bool hasNegative = c0 < -EarClipEpsilon || c1 < -EarClipEpsilon || c2 < -EarClipEpsilon;

            return !(hasPositive && hasNegative);
        }

        private static float Cross2(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
