using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace SangriaMesh
{
    public static class SangriaMeshSphereGenerator
    {
        public static NativeDetail CreateUvSphere(
            float radius,
            int longitudeSegments,
            int latitudeSegments,
            Allocator allocator = Allocator.Persistent)
        {
            CreateUvSphere(out var detail, radius, longitudeSegments, latitudeSegments, allocator);
            return detail;
        }

        public static void CreateUvSphere(
            out NativeDetail detail,
            float radius,
            int longitudeSegments,
            int latitudeSegments,
            Allocator allocator = Allocator.Persistent)
        {
            ValidateInputs(radius, longitudeSegments, latitudeSegments);
            GetUvSphereTopologyCounts(longitudeSegments, latitudeSegments, out int pointCount, out int vertexCount, out int primitiveCount);

            detail = new NativeDetail(pointCount, vertexCount, primitiveCount, allocator);
            PopulateUvSphere(ref detail, radius, longitudeSegments, latitudeSegments);
        }

        public static void PopulateUvSphere(
            ref NativeDetail detail,
            float radius,
            int longitudeSegments,
            int latitudeSegments)
        {
            ValidateInputs(radius, longitudeSegments, latitudeSegments);

            int interiorRingCount = latitudeSegments - 1;
            GetUvSphereTopologyCounts(longitudeSegments, latitudeSegments, out int pointCount, out int vertexCount, out int primitiveCount);

            EnsurePointAttribute<float3>(ref detail, AttributeID.Normal, "Add point normal attribute");
            EnsureVertexAttribute<float3>(ref detail, AttributeID.Normal, "Add vertex normal attribute");
            EnsureVertexAttribute<float2>(ref detail, AttributeID.UV0, "Add vertex UV0 attribute");

            detail.Clear();
            detail.AllocateDenseTopologyUnchecked(pointCount, vertexCount, primitiveCount, prepareTriangleStorage: true);

            Ensure(detail.TryGetPointAccessor<float3>(AttributeID.Position, out var pointPositionAccessor), "Get point position accessor");
            Ensure(detail.TryGetPointAccessor<float3>(AttributeID.Normal, out var pointNormalAccessor), "Get point normal accessor");
            Ensure(detail.TryGetVertexAccessor<float3>(AttributeID.Normal, out var vertexNormalAccessor), "Get vertex normal accessor");
            Ensure(detail.TryGetVertexAccessor<float2>(AttributeID.UV0, out var vertexUvAccessor), "Get vertex uv accessor");

            unsafe
            {
                var buildJob = new SphereDenseBuildJob
                {
                    Radius = radius,
                    LongitudeSegments = longitudeSegments,
                    LatitudeSegments = latitudeSegments,
                    InteriorRingCount = interiorRingCount,
                    RingVertexStride = longitudeSegments + 1,
                    PointPositionPtr = pointPositionAccessor.GetBasePointer(),
                    PointNormalPtr = pointNormalAccessor.GetBasePointer(),
                    VertexNormalPtr = vertexNormalAccessor.GetBasePointer(),
                    VertexUvPtr = vertexUvAccessor.GetBasePointer(),
                    VertexToPointPtr = detail.GetVertexToPointPointerUnchecked(),
                    PrimitiveDataPtr = detail.GetPrimitiveTriangleDataPointerUnchecked()
                };

                buildJob.Run();
            }

            detail.MarkTopologyAndAttributeChanged();
        }

        public static void GetUvSphereTopologyCounts(
            int longitudeSegments,
            int latitudeSegments,
            out int pointCount,
            out int vertexCount,
            out int primitiveCount)
        {
            if (longitudeSegments < 3)
                throw new ArgumentOutOfRangeException(nameof(longitudeSegments), "Longitude segments must be >= 3.");
            if (latitudeSegments < 3)
                throw new ArgumentOutOfRangeException(nameof(latitudeSegments), "Latitude segments must be >= 3.");

            int interiorRingCount = latitudeSegments - 1;
            pointCount = 2 + interiorRingCount * longitudeSegments;
            vertexCount = 2 + interiorRingCount * (longitudeSegments + 1);
            primitiveCount = 2 * longitudeSegments * interiorRingCount;
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true)]
        private unsafe struct SphereDenseBuildJob : IJob
        {
            public float Radius;
            public int LongitudeSegments;
            public int LatitudeSegments;
            public int InteriorRingCount;
            public int RingVertexStride;

            [NativeDisableUnsafePtrRestriction] public float3* PointPositionPtr;
            [NativeDisableUnsafePtrRestriction] public float3* PointNormalPtr;
            [NativeDisableUnsafePtrRestriction] public float3* VertexNormalPtr;
            [NativeDisableUnsafePtrRestriction] public float2* VertexUvPtr;
            [NativeDisableUnsafePtrRestriction] public int* VertexToPointPtr;
            [NativeDisableUnsafePtrRestriction] public int* PrimitiveDataPtr;

            public void Execute()
            {
                const int northPolePoint = 0;
                const int southPolePoint = 1;
                const int northPoleVertex = 0;
                const int southPoleVertex = 1;

                PointPositionPtr[northPolePoint] = new float3(0f, Radius, 0f);
                PointPositionPtr[southPolePoint] = new float3(0f, -Radius, 0f);
                PointNormalPtr[northPolePoint] = new float3(0f, 1f, 0f);
                PointNormalPtr[southPolePoint] = new float3(0f, -1f, 0f);

                VertexToPointPtr[northPoleVertex] = northPolePoint;
                VertexToPointPtr[southPoleVertex] = southPolePoint;
                VertexNormalPtr[northPoleVertex] = PointNormalPtr[northPolePoint];
                VertexNormalPtr[southPoleVertex] = PointNormalPtr[southPolePoint];
                VertexUvPtr[northPoleVertex] = new float2(0.5f, 0f);
                VertexUvPtr[southPoleVertex] = new float2(0.5f, 1f);

                float deltaTheta = (2f * math.PI) / LongitudeSegments;
                math.sincos(deltaTheta, out float sinDelta, out float cosDelta);

                for (int ring = 0; ring < InteriorRingCount; ring++)
                {
                    float v = (float)(ring + 1) / LatitudeSegments;
                    float phi = v * math.PI;
                    math.sincos(phi, out float sinPhi, out float cosPhi);

                    int pointRingStart = 2 + ring * LongitudeSegments;
                    int vertexRingStart = 2 + ring * RingVertexStride;

                    float s = 0f;
                    float c = 1f;
                    for (int lon = 0; lon < LongitudeSegments; lon++)
                    {
                        int pointIndex = pointRingStart + lon;
                        int vertexIndex = vertexRingStart + lon;

                        float3 normal = new float3(c * sinPhi, cosPhi, s * sinPhi);
                        PointPositionPtr[pointIndex] = normal * Radius;
                        PointNormalPtr[pointIndex] = normal;

                        VertexToPointPtr[vertexIndex] = pointIndex;
                        VertexNormalPtr[vertexIndex] = normal;
                        VertexUvPtr[vertexIndex] = new float2((float)lon / LongitudeSegments, v);

                        float nextS = s * cosDelta + c * sinDelta;
                        float nextC = c * cosDelta - s * sinDelta;
                        s = nextS;
                        c = nextC;
                    }

                    int seamVertexIndex = vertexRingStart + LongitudeSegments;
                    VertexToPointPtr[seamVertexIndex] = pointRingStart;
                    VertexNormalPtr[seamVertexIndex] = PointNormalPtr[pointRingStart];
                    VertexUvPtr[seamVertexIndex] = new float2(1f, v);
                }

                int primitiveCursor = 0;

                int firstRingVertexStart = 2;
                for (int lon = 0; lon < LongitudeSegments; lon++)
                {
                    int current = firstRingVertexStart + lon;
                    int next = current + 1;
                    WriteTriangle(primitiveCursor++, northPoleVertex, next, current);
                }

                for (int ring = 0; ring < InteriorRingCount - 1; ring++)
                {
                    int ringStart = 2 + ring * RingVertexStride;
                    int nextRingStart = ringStart + RingVertexStride;

                    for (int lon = 0; lon < LongitudeSegments; lon++)
                    {
                        int a = ringStart + lon;
                        int b = a + 1;
                        int c = nextRingStart + lon;
                        int d = c + 1;

                        WriteTriangle(primitiveCursor++, a, d, c);
                        WriteTriangle(primitiveCursor++, a, b, d);
                    }
                }

                int lastRingStart = 2 + (InteriorRingCount - 1) * RingVertexStride;
                for (int lon = 0; lon < LongitudeSegments; lon++)
                {
                    int current = lastRingStart + lon;
                    int next = current + 1;
                    WriteTriangle(primitiveCursor++, southPoleVertex, current, next);
                }
            }

            private void WriteTriangle(int primitiveIndex, int v0, int v1, int v2)
            {
                int triStart = primitiveIndex * 3;
                PrimitiveDataPtr[triStart] = v0;
                PrimitiveDataPtr[triStart + 1] = v1;
                PrimitiveDataPtr[triStart + 2] = v2;
            }
        }

        private static void ValidateInputs(float radius, int longitudeSegments, int latitudeSegments)
        {
            if (radius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be > 0.");
            if (longitudeSegments < 3)
                throw new ArgumentOutOfRangeException(nameof(longitudeSegments), "Longitude segments must be >= 3.");
            if (latitudeSegments < 3)
                throw new ArgumentOutOfRangeException(nameof(latitudeSegments), "Latitude segments must be >= 3.");
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
