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
        private const int ParallelRingThreshold = 8;
        private const int ParallelPrimitiveThreshold = 4096;

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
            detail.AllocateDenseTopologyUnchecked(
                pointCount,
                vertexCount,
                primitiveCount,
                prepareTriangleStorage: true,
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
                int* primitiveDataPtr = detail.GetPrimitiveTriangleDataPointerUnchecked();

                if (ShouldUseParallelBuild(interiorRingCount, primitiveCount))
                {
                    InitializePoleData(radius, pointPositionPtr, pointNormalPtr, vertexNormalPtr, vertexUvPtr, vertexToPointPtr);

                    var ringJob = new SphereInteriorRingBuildJob
                    {
                        Radius = radius,
                        LongitudeSegments = longitudeSegments,
                        LatitudeSegments = latitudeSegments,
                        RingVertexStride = longitudeSegments + 1,
                        PointPositionPtr = pointPositionPtr,
                        PointNormalPtr = pointNormalPtr,
                        VertexNormalPtr = vertexNormalPtr,
                        VertexUvPtr = vertexUvPtr,
                        VertexToPointPtr = vertexToPointPtr
                    };

                    var triangleJob = new SpherePrimitiveBuildJob
                    {
                        LongitudeSegments = longitudeSegments,
                        InteriorRingCount = interiorRingCount,
                        RingVertexStride = longitudeSegments + 1,
                        PrimitiveDataPtr = primitiveDataPtr
                    };

                    JobHandle ringHandle = ringJob.Schedule(interiorRingCount, 1);
                    JobHandle triangleHandle = triangleJob.Schedule(primitiveCount, 128);
                    JobHandle.CombineDependencies(ringHandle, triangleHandle).Complete();
                }
                else
                {
                    var buildJob = new SphereDenseBuildJob
                    {
                        Radius = radius,
                        LongitudeSegments = longitudeSegments,
                        LatitudeSegments = latitudeSegments,
                        InteriorRingCount = interiorRingCount,
                        RingVertexStride = longitudeSegments + 1,
                        PointPositionPtr = pointPositionPtr,
                        PointNormalPtr = pointNormalPtr,
                        VertexNormalPtr = vertexNormalPtr,
                        VertexUvPtr = vertexUvPtr,
                        VertexToPointPtr = vertexToPointPtr,
                        PrimitiveDataPtr = primitiveDataPtr
                    };

                    buildJob.Run();
                }
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

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true)]
        private unsafe struct SphereInteriorRingBuildJob : IJobParallelFor
        {
            public float Radius;
            public int LongitudeSegments;
            public int LatitudeSegments;
            public int RingVertexStride;

            [NativeDisableUnsafePtrRestriction] public float3* PointPositionPtr;
            [NativeDisableUnsafePtrRestriction] public float3* PointNormalPtr;
            [NativeDisableUnsafePtrRestriction] public float3* VertexNormalPtr;
            [NativeDisableUnsafePtrRestriction] public float2* VertexUvPtr;
            [NativeDisableUnsafePtrRestriction] public int* VertexToPointPtr;

            public void Execute(int ring)
            {
                float v = (float)(ring + 1) / LatitudeSegments;
                float phi = v * math.PI;
                math.sincos(phi, out float sinPhi, out float cosPhi);

                int pointRingStart = 2 + ring * LongitudeSegments;
                int vertexRingStart = 2 + ring * RingVertexStride;
                float invLongitude = 1f / LongitudeSegments;
                float deltaTheta = 2f * math.PI * invLongitude;
                math.sincos(deltaTheta, out float sinDelta, out float cosDelta);

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
                    VertexUvPtr[vertexIndex] = new float2(lon * invLongitude, v);

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
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true)]
        private unsafe struct SpherePrimitiveBuildJob : IJobParallelFor
        {
            public int LongitudeSegments;
            public int InteriorRingCount;
            public int RingVertexStride;

            [NativeDisableUnsafePtrRestriction] public int* PrimitiveDataPtr;

            public void Execute(int primitiveIndex)
            {
                const int northPoleVertex = 0;
                const int southPoleVertex = 1;

                int topCount = LongitudeSegments;
                int middleCount = (InteriorRingCount - 1) * LongitudeSegments * 2;

                if (primitiveIndex < topCount)
                {
                    int lon = primitiveIndex;
                    int current = 2 + lon;
                    int next = current + 1;
                    WriteTriangle(primitiveIndex, northPoleVertex, next, current);
                    return;
                }

                int middleStart = topCount;
                int middleEnd = middleStart + middleCount;
                if (primitiveIndex < middleEnd)
                {
                    int local = primitiveIndex - middleStart;
                    int pairStride = LongitudeSegments * 2;
                    int ring = local / pairStride;
                    int inRing = local - ring * pairStride;
                    int lon = inRing >> 1;
                    bool secondTri = (inRing & 1) != 0;

                    int ringStart = 2 + ring * RingVertexStride;
                    int nextRingStart = ringStart + RingVertexStride;
                    int a = ringStart + lon;
                    int b = a + 1;
                    int c = nextRingStart + lon;
                    int d = c + 1;

                    if (secondTri)
                        WriteTriangle(primitiveIndex, a, b, d);
                    else
                        WriteTriangle(primitiveIndex, a, d, c);

                    return;
                }

                int bottomLon = primitiveIndex - middleEnd;
                int lastRingStart = 2 + (InteriorRingCount - 1) * RingVertexStride;
                int currentBottom = lastRingStart + bottomLon;
                int nextBottom = currentBottom + 1;
                WriteTriangle(primitiveIndex, southPoleVertex, currentBottom, nextBottom);
            }

            private void WriteTriangle(int primitiveIndex, int v0, int v1, int v2)
            {
                int triStart = primitiveIndex * 3;
                PrimitiveDataPtr[triStart] = v0;
                PrimitiveDataPtr[triStart + 1] = v1;
                PrimitiveDataPtr[triStart + 2] = v2;
            }
        }

        private static bool ShouldUseParallelBuild(int interiorRingCount, int primitiveCount)
        {
            return interiorRingCount >= ParallelRingThreshold && primitiveCount >= ParallelPrimitiveThreshold;
        }

        private static unsafe void InitializePoleData(
            float radius,
            float3* pointPositionPtr,
            float3* pointNormalPtr,
            float3* vertexNormalPtr,
            float2* vertexUvPtr,
            int* vertexToPointPtr)
        {
            const int northPolePoint = 0;
            const int southPolePoint = 1;
            const int northPoleVertex = 0;
            const int southPoleVertex = 1;

            pointPositionPtr[northPolePoint] = new float3(0f, radius, 0f);
            pointPositionPtr[southPolePoint] = new float3(0f, -radius, 0f);
            pointNormalPtr[northPolePoint] = new float3(0f, 1f, 0f);
            pointNormalPtr[southPolePoint] = new float3(0f, -1f, 0f);

            vertexToPointPtr[northPoleVertex] = northPolePoint;
            vertexToPointPtr[southPoleVertex] = southPolePoint;
            vertexNormalPtr[northPoleVertex] = pointNormalPtr[northPolePoint];
            vertexNormalPtr[southPoleVertex] = pointNormalPtr[southPolePoint];
            vertexUvPtr[northPoleVertex] = new float2(0.5f, 0f);
            vertexUvPtr[southPoleVertex] = new float2(0.5f, 1f);
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
