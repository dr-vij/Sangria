using System;
using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    public static class SangriaMeshBoxGenerator
    {
        private const int BoxPointCount = 8;
        private const int BoxVertexCount = 24;
        private const int BoxPrimitiveCount = 6;

        public static NativeDetail CreateBox(
            float3 size,
            Allocator allocator = Allocator.Persistent)
        {
            CreateBox(out var detail, size, allocator);
            return detail;
        }

        public static NativeDetail CreateBox(
            float width,
            float height,
            float depth,
            Allocator allocator = Allocator.Persistent)
        {
            CreateBox(out var detail, width, height, depth, allocator);
            return detail;
        }

        public static void CreateBox(
            out NativeDetail detail,
            float3 size,
            Allocator allocator = Allocator.Persistent)
        {
            ValidateInputs(size);
            GetBoxTopologyCounts(out int pointCount, out int vertexCount, out int primitiveCount);

            detail = new NativeDetail(pointCount, vertexCount, primitiveCount, allocator);
            PopulateBox(ref detail, size);
        }

        public static void CreateBox(
            out NativeDetail detail,
            float width,
            float height,
            float depth,
            Allocator allocator = Allocator.Persistent)
        {
            CreateBox(out detail, new float3(width, height, depth), allocator);
        }

        public static void PopulateBox(
            ref NativeDetail detail,
            float width,
            float height,
            float depth)
        {
            PopulateBox(ref detail, new float3(width, height, depth));
        }

        public static void PopulateBox(
            ref NativeDetail detail,
            float3 size)
        {
            ValidateInputs(size);
            GetBoxTopologyCounts(out int pointCount, out int vertexCount, out int primitiveCount);

            EnsurePointAttribute<float3>(ref detail, AttributeID.Normal, "Add point normal attribute");
            EnsureVertexAttribute<float3>(ref detail, AttributeID.Normal, "Add vertex normal attribute");
            EnsureVertexAttribute<float2>(ref detail, AttributeID.UV0, "Add vertex UV0 attribute");

            detail.Clear();
            detail.AllocateDenseTopologyUnchecked(
                pointCount,
                vertexCount,
                0,
                prepareTriangleStorage: false,
                initializeVertexToPoint: false);

            Ensure(detail.TryGetPointAccessor<float3>(AttributeID.Position, out var pointPositionAccessor), "Get point position accessor");
            Ensure(detail.TryGetPointAccessor<float3>(AttributeID.Normal, out var pointNormalAccessor), "Get point normal accessor");
            Ensure(detail.TryGetVertexAccessor<float3>(AttributeID.Normal, out var vertexNormalAccessor), "Get vertex normal accessor");
            Ensure(detail.TryGetVertexAccessor<float2>(AttributeID.UV0, out var vertexUvAccessor), "Get vertex uv accessor");

            unsafe
            {
                float3* pointPositionPtr = pointPositionAccessor.GetBasePointer();
                float3* pointNormalPtr = pointNormalAccessor.GetBasePointer();
                float3* vertexNormalPtr = vertexNormalAccessor.GetBasePointer();
                float2* vertexUvPtr = vertexUvAccessor.GetBasePointer();
                int* vertexToPointPtr = detail.GetVertexToPointPointerUnchecked();

                float3 halfSize = size * 0.5f;

                pointPositionPtr[0] = new float3(-halfSize.x, -halfSize.y, -halfSize.z);
                pointPositionPtr[1] = new float3(halfSize.x, -halfSize.y, -halfSize.z);
                pointPositionPtr[2] = new float3(halfSize.x, halfSize.y, -halfSize.z);
                pointPositionPtr[3] = new float3(-halfSize.x, halfSize.y, -halfSize.z);
                pointPositionPtr[4] = new float3(-halfSize.x, -halfSize.y, halfSize.z);
                pointPositionPtr[5] = new float3(halfSize.x, -halfSize.y, halfSize.z);
                pointPositionPtr[6] = new float3(halfSize.x, halfSize.y, halfSize.z);
                pointPositionPtr[7] = new float3(-halfSize.x, halfSize.y, halfSize.z);

                for (int pointIndex = 0; pointIndex < BoxPointCount; pointIndex++)
                    pointNormalPtr[pointIndex] = math.normalize(pointPositionPtr[pointIndex]);

                int vertexCursor = 0;

                // Corners stay shared as points; per-face vertices preserve hard normals and UV seams.
                WriteFace(
                    ref detail,
                    ref vertexCursor,
                    4, 5, 6, 7,
                    new float3(0f, 0f, 1f),
                    vertexNormalPtr,
                    vertexUvPtr,
                    vertexToPointPtr);
                WriteFace(
                    ref detail,
                    ref vertexCursor,
                    1, 0, 3, 2,
                    new float3(0f, 0f, -1f),
                    vertexNormalPtr,
                    vertexUvPtr,
                    vertexToPointPtr);
                WriteFace(
                    ref detail,
                    ref vertexCursor,
                    0, 4, 7, 3,
                    new float3(-1f, 0f, 0f),
                    vertexNormalPtr,
                    vertexUvPtr,
                    vertexToPointPtr);
                WriteFace(
                    ref detail,
                    ref vertexCursor,
                    5, 1, 2, 6,
                    new float3(1f, 0f, 0f),
                    vertexNormalPtr,
                    vertexUvPtr,
                    vertexToPointPtr);
                WriteFace(
                    ref detail,
                    ref vertexCursor,
                    3, 7, 6, 2,
                    new float3(0f, 1f, 0f),
                    vertexNormalPtr,
                    vertexUvPtr,
                    vertexToPointPtr);
                WriteFace(
                    ref detail,
                    ref vertexCursor,
                    0, 1, 5, 4,
                    new float3(0f, -1f, 0f),
                    vertexNormalPtr,
                    vertexUvPtr,
                    vertexToPointPtr);
            }

            detail.MarkTopologyAndAttributeChanged();
        }

        public static void GetBoxTopologyCounts(
            out int pointCount,
            out int vertexCount,
            out int primitiveCount)
        {
            pointCount = BoxPointCount;
            vertexCount = BoxVertexCount;
            primitiveCount = BoxPrimitiveCount;
        }

        private static unsafe void WriteFace(
            ref NativeDetail detail,
            ref int vertexCursor,
            int point0,
            int point1,
            int point2,
            int point3,
            float3 normal,
            float3* vertexNormalPtr,
            float2* vertexUvPtr,
            int* vertexToPointPtr)
        {
            int v0 = vertexCursor++;
            int v1 = vertexCursor++;
            int v2 = vertexCursor++;
            int v3 = vertexCursor++;

            vertexToPointPtr[v0] = point0;
            vertexToPointPtr[v1] = point1;
            vertexToPointPtr[v2] = point2;
            vertexToPointPtr[v3] = point3;

            vertexNormalPtr[v0] = normal;
            vertexNormalPtr[v1] = normal;
            vertexNormalPtr[v2] = normal;
            vertexNormalPtr[v3] = normal;

            vertexUvPtr[v0] = new float2(0f, 0f);
            vertexUvPtr[v1] = new float2(1f, 0f);
            vertexUvPtr[v2] = new float2(1f, 1f);
            vertexUvPtr[v3] = new float2(0f, 1f);

            var faceVertices = new NativeArray<int>(4, Allocator.Temp);
            faceVertices[0] = v0;
            faceVertices[1] = v1;
            faceVertices[2] = v2;
            faceVertices[3] = v3;

            int primitiveIndex = detail.AddPrimitive(faceVertices);
            faceVertices.Dispose();

            if (primitiveIndex < 0)
                throw new InvalidOperationException("Add face primitive failed.");
        }

        private static void ValidateInputs(float3 size)
        {
            if (math.any(size <= 0f))
                throw new ArgumentOutOfRangeException(nameof(size), "All box size components must be > 0.");
        }

        private static void EnsurePointAttribute<T>(ref NativeDetail detail, int attributeId, string operation) where T : unmanaged
        {
            if (!detail.HasPointAttribute(attributeId))
                Ensure(detail.AddPointAttribute<T>(attributeId), operation);
        }

        private static void EnsureVertexAttribute<T>(ref NativeDetail detail, int attributeId, string operation) where T : unmanaged
        {
            if (!detail.HasVertexAttribute(attributeId))
                Ensure(detail.AddVertexAttribute<T>(attributeId), operation);
        }

        private static void Ensure(CoreResult result, string operation)
        {
            if (result != CoreResult.Success)
                throw new InvalidOperationException($"{operation} failed: {result}");
        }
    }
}
