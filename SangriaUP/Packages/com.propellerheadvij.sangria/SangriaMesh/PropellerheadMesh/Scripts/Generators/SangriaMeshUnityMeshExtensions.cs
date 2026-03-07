using System;
using System.Collections.Generic;
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
        private static readonly VertexAttributeDescriptor[] TriangleLayoutPositionOnly =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0)
        };

        private static readonly VertexAttributeDescriptor[] TriangleLayoutPositionNormal =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1)
        };

        private static readonly VertexAttributeDescriptor[] TriangleLayoutPositionUv =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1)
        };

        private static readonly VertexAttributeDescriptor[] TriangleLayoutPositionNormalUv =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2)
        };

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
        public static void FillUnityMesh(this NativeDetail detail, Mesh mesh, Allocator allocator = Allocator.Temp)
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
        public static void FillUnityMesh(this NativeCompiledDetail compiled, Mesh mesh)
        {
            FillUnityMeshInternal(compiled, mesh, assumeTriangleTopology: false);
        }

        /// <summary>
        /// Fast path for known triangle-only topology.
        /// </summary>
        public static void FillUnityMeshTriangles(this NativeCompiledDetail compiled, Mesh mesh)
        {
            FillUnityMeshTrianglesMeshData(compiled, mesh);
        }

        private static unsafe void FillUnityMeshTrianglesMeshData(NativeCompiledDetail compiled, Mesh mesh)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            mesh.Clear();

            if (compiled.VertexCount <= 0 || compiled.PrimitiveCount <= 0)
                return;
            if (!IsTriangleOnlyTopology(compiled))
                throw new InvalidOperationException("FillUnityMeshTriangles requires triangle-only topology. Use FillUnityMesh for polygon primitives.");

            if (compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var pointPositions) != CoreResult.Success)
                throw new InvalidOperationException("Point position attribute is required for mesh conversion.");

            bool hasVertexNormals =
                compiled.TryGetAttributeAccessor<float3>(MeshDomain.Vertex, AttributeID.Normal, out var vertexNormals) == CoreResult.Success;
            bool hasPointNormals =
                compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Normal, out var pointNormals) == CoreResult.Success;
            bool hasVertexUv0 =
                compiled.TryGetAttributeAccessor<float2>(MeshDomain.Vertex, AttributeID.UV0, out var vertexUv0) == CoreResult.Success;

            int vertexCount = compiled.VertexCount;
            int indexCount = compiled.PrimitiveVerticesDense.Length;
            bool hasNormals = hasVertexNormals || hasPointNormals;
            IndexFormat indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            var layout = ResolveTriangleVertexLayout(hasNormals, hasVertexUv0);

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            bool meshDataApplied = false;
            try
            {
                var meshData = meshDataArray[0];
                meshData.SetVertexBufferParams(vertexCount, layout);
                meshData.SetIndexBufferParams(indexCount, indexFormat);

                var positionData = meshData.GetVertexData<float3>(0);
                float3* positionDst = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(positionData);

                float3* normalDst = null;
                if (hasNormals)
                {
                    var normalData = meshData.GetVertexData<float3>(1);
                    normalDst = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(normalData);
                }

                float2* uvDst = null;
                if (hasVertexUv0)
                {
                    int uvStream = hasNormals ? 2 : 1;
                    var uvData = meshData.GetVertexData<float2>(uvStream);
                    uvDst = (float2*)NativeArrayUnsafeUtility.GetUnsafePtr(uvData);
                }

                int* vertexToPointPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(compiled.VertexToPointDense);
                float3* pointPositionsPtr = pointPositions.GetBasePointer();
                float3* pointNormalsPtr = hasPointNormals ? pointNormals.GetBasePointer() : null;
                float3* vertexNormalsPtr = hasVertexNormals ? vertexNormals.GetBasePointer() : null;
                float2* vertexUv0Ptr = hasVertexUv0 ? vertexUv0.GetBasePointer() : null;
                int pointCount = pointPositions.Length;

                float minX = 0f, minY = 0f, minZ = 0f;
                float maxX = 0f, maxY = 0f, maxZ = 0f;
                bool hasBounds = false;

                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    int pointIndex = vertexToPointPtr[vertexIndex];
                    bool pointIndexValid = (uint)pointIndex < (uint)pointCount;
                    float3 position = pointIndexValid ? pointPositionsPtr[pointIndex] : default;
                    positionDst[vertexIndex] = position;

                    if (!hasBounds)
                    {
                        minX = maxX = position.x;
                        minY = maxY = position.y;
                        minZ = maxZ = position.z;
                        hasBounds = true;
                    }
                    else
                    {
                        if (position.x < minX) minX = position.x;
                        else if (position.x > maxX) maxX = position.x;

                        if (position.y < minY) minY = position.y;
                        else if (position.y > maxY) maxY = position.y;

                        if (position.z < minZ) minZ = position.z;
                        else if (position.z > maxZ) maxZ = position.z;
                    }

                    if (hasNormals)
                    {
                        normalDst[vertexIndex] = hasVertexNormals
                            ? vertexNormalsPtr[vertexIndex]
                            : (pointIndexValid ? pointNormalsPtr[pointIndex] : default);
                    }

                    if (hasVertexUv0)
                        uvDst[vertexIndex] = vertexUv0Ptr[vertexIndex];
                }

                if (indexFormat == IndexFormat.UInt32)
                {
                    var indexData = meshData.GetIndexData<int>();
                    UnsafeUtility.MemCpy(
                        NativeArrayUnsafeUtility.GetUnsafePtr(indexData),
                        NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(compiled.PrimitiveVerticesDense),
                        indexCount * UnsafeUtility.SizeOf<int>());
                }
                else
                {
                    var indexData = meshData.GetIndexData<ushort>();
                    ushort* indexDst = (ushort*)NativeArrayUnsafeUtility.GetUnsafePtr(indexData);
                    int* indexSrc = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(compiled.PrimitiveVerticesDense);

                    for (int i = 0; i < indexCount; i++)
                        indexDst[i] = (ushort)indexSrc[i];
                }

                meshData.subMeshCount = 1;
                var subMeshDescriptor = new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
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

        private static unsafe void FillUnityMeshInternal(NativeCompiledDetail compiled, Mesh mesh, bool assumeTriangleTopology)
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

            bool hasVertexNormals =
                compiled.TryGetAttributeAccessor<float3>(MeshDomain.Vertex, AttributeID.Normal, out var vertexNormals) == CoreResult.Success;
            bool hasPointNormals =
                compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Normal, out var pointNormals) == CoreResult.Success;
            bool hasVertexUv0 =
                compiled.TryGetAttributeAccessor<float2>(MeshDomain.Vertex, AttributeID.UV0, out var vertexUv0) == CoreResult.Success;
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

            int[] triangles = new int[triangleIndexCount];
            int triangleWriteIndex = 0;
            var primitiveVertices = new List<int>(16);
            var primitivePositions = new List<float3>(16);
            var projectedPolygon = new List<float2>(16);
            var polygonOrder = new List<int>(16);

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
                    triangleWriteIndex = WriteFanTriangulation(primitiveVertices, triangles, triangleWriteIndex);
                    continue;
                }

                if (!TryTriangulateEarClip(primitiveVertices, projectedPolygon, polygonOrder, triangles, ref triangleWriteIndex))
                    triangleWriteIndex = WriteFanTriangulation(primitiveVertices, triangles, triangleWriteIndex);
            }

            int vertexCount = compiled.VertexCount;
            int indexCount = triangles.Length;
            bool hasNormals = hasVertexNormals || hasPointNormals;
            IndexFormat indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            var layout = ResolveTriangleVertexLayout(hasNormals, hasVertexUv0);

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            bool meshDataApplied = false;
            try
            {
                var meshData = meshDataArray[0];
                meshData.SetVertexBufferParams(vertexCount, layout);
                meshData.SetIndexBufferParams(indexCount, indexFormat);

                var positionData = meshData.GetVertexData<float3>(0);
                float3* positionDst = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(positionData);

                float3* normalDst = null;
                if (hasNormals)
                {
                    var normalData = meshData.GetVertexData<float3>(1);
                    normalDst = (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(normalData);
                }

                float2* uvDst = null;
                if (hasVertexUv0)
                {
                    int uvStream = hasNormals ? 2 : 1;
                    var uvData = meshData.GetVertexData<float2>(uvStream);
                    uvDst = (float2*)NativeArrayUnsafeUtility.GetUnsafePtr(uvData);
                }

                int* vertexToPointPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(compiled.VertexToPointDense);
                float3* pointPositionsPtr = pointPositions.GetBasePointer();
                float3* pointNormalsPtr = hasPointNormals ? pointNormals.GetBasePointer() : null;
                float3* vertexNormalsPtr = hasVertexNormals ? vertexNormals.GetBasePointer() : null;
                float2* vertexUv0Ptr = hasVertexUv0 ? vertexUv0.GetBasePointer() : null;

                float minX = 0f, minY = 0f, minZ = 0f;
                float maxX = 0f, maxY = 0f, maxZ = 0f;
                bool hasBounds = false;

                for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
                {
                    int pointIndex = vertexToPointPtr[vertexIndex];
                    bool pointIndexValid = (uint)pointIndex < (uint)pointCount;
                    float3 position = pointIndexValid ? pointPositionsPtr[pointIndex] : default;
                    positionDst[vertexIndex] = position;

                    if (!hasBounds)
                    {
                        minX = maxX = position.x;
                        minY = maxY = position.y;
                        minZ = maxZ = position.z;
                        hasBounds = true;
                    }
                    else
                    {
                        if (position.x < minX) minX = position.x;
                        else if (position.x > maxX) maxX = position.x;

                        if (position.y < minY) minY = position.y;
                        else if (position.y > maxY) maxY = position.y;

                        if (position.z < minZ) minZ = position.z;
                        else if (position.z > maxZ) maxZ = position.z;
                    }

                    if (hasNormals)
                    {
                        normalDst[vertexIndex] = hasVertexNormals
                            ? vertexNormalsPtr[vertexIndex]
                            : (pointIndexValid ? pointNormalsPtr[pointIndex] : default);
                    }

                    if (hasVertexUv0)
                        uvDst[vertexIndex] = vertexUv0Ptr[vertexIndex];
                }

                if (indexFormat == IndexFormat.UInt32)
                {
                    var indexData = meshData.GetIndexData<int>();
                    fixed (int* indexSrc = triangles)
                    {
                        UnsafeUtility.MemCpy(
                            NativeArrayUnsafeUtility.GetUnsafePtr(indexData),
                            indexSrc,
                            indexCount * UnsafeUtility.SizeOf<int>());
                    }
                }
                else
                {
                    var indexData = meshData.GetIndexData<ushort>();
                    ushort* indexDst = (ushort*)NativeArrayUnsafeUtility.GetUnsafePtr(indexData);
                    for (int i = 0; i < indexCount; i++)
                        indexDst[i] = (ushort)triangles[i];
                }

                meshData.subMeshCount = 1;
                var subMeshDescriptor = new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
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

        private static VertexAttributeDescriptor[] ResolveTriangleVertexLayout(bool hasNormals, bool hasUv0)
        {
            if (hasNormals)
                return hasUv0 ? TriangleLayoutPositionNormalUv : TriangleLayoutPositionNormal;

            return hasUv0 ? TriangleLayoutPositionUv : TriangleLayoutPositionOnly;
        }

        private static bool IsTriangleOnlyTopology(NativeCompiledDetail compiled)
        {
            return compiled.IsTriangleOnlyTopology;
        }

        /// <summary>
        /// Creates a new Unity Mesh from editable SangriaMesh detail.
        /// </summary>
        public static Mesh ToUnityMesh(this NativeDetail detail, string meshName = "SangriaMesh", Allocator allocator = Allocator.Temp)
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
        public static Mesh ToUnityMesh(this NativeCompiledDetail compiled, string meshName = "SangriaMesh")
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
        public static Mesh ToUnityMeshTriangles(this NativeCompiledDetail compiled, string meshName = "SangriaMesh")
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

        private static bool TryBuildProjectedPolygon(List<float3> positions, List<float2> projectedPolygon)
        {
            projectedPolygon.Clear();
            int count = positions.Count;
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
            if (absNormal.x <= EarClipEpsilon && absNormal.y <= EarClipEpsilon && absNormal.z <= EarClipEpsilon)
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
            List<int> primitiveVertices,
            List<float2> projectedPolygon,
            List<int> polygonOrder,
            int[] triangles,
            ref int triangleWriteIndex)
        {
            polygonOrder.Clear();
            int vertexCount = primitiveVertices.Count;
            for (int i = 0; i < vertexCount; i++)
                polygonOrder.Add(i);

            float area2 = ComputeSignedArea2(projectedPolygon, polygonOrder);
            if (math.abs(area2) <= EarClipEpsilon)
                return false;

            bool isCcw = area2 > 0f;
            int guard = 0;
            int guardLimit = vertexCount * vertexCount;

            while (polygonOrder.Count > 3 && guard++ < guardLimit)
            {
                bool earFound = false;
                int count = polygonOrder.Count;

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

            if (polygonOrder.Count != 3)
                return false;

            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[0]];
            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[1]];
            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[2]];
            return true;
        }

        private static int WriteFanTriangulation(List<int> primitiveVertices, int[] triangles, int triangleWriteIndex)
        {
            if (primitiveVertices.Count < 3)
                return triangleWriteIndex;

            int root = primitiveVertices[0];
            for (int i = 1; i < primitiveVertices.Count - 1; i++)
            {
                triangles[triangleWriteIndex++] = root;
                triangles[triangleWriteIndex++] = primitiveVertices[i];
                triangles[triangleWriteIndex++] = primitiveVertices[i + 1];
            }

            return triangleWriteIndex;
        }

        private static float ComputeSignedArea2(List<float2> points, List<int> order)
        {
            float sum = 0f;
            int count = order.Count;

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
