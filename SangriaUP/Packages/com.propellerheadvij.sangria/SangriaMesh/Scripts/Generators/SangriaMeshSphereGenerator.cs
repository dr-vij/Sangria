using System;
using System.Diagnostics;
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

                if (ShouldUseParallelBuild(interiorRingCount, primitiveCount))
                    ringJob.Schedule(interiorRingCount, 1).Complete();
                else
                    ringJob.Run(interiorRingCount);
            }

            var primitiveVertices = new NativeArray<int4>(primitiveCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var primitiveSizes = new NativeArray<byte>(primitiveCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var primitiveBuildJob = new SpherePrimitivePolygonBuildJob
            {
                LongitudeSegments = longitudeSegments,
                InteriorRingCount = interiorRingCount,
                RingVertexStride = longitudeSegments + 1,
                PrimitiveVertices = primitiveVertices,
                PrimitiveSizes = primitiveSizes
            };

            if (ShouldUseParallelBuild(interiorRingCount, primitiveCount))
                primitiveBuildJob.Schedule(primitiveCount, 128).Complete();
            else
                primitiveBuildJob.Run(primitiveCount);

            var triangle = new NativeArray<int>(3, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var quad = new NativeArray<int>(4, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int primitiveIndex = 0; primitiveIndex < primitiveCount; primitiveIndex++)
            {
                int4 vertices = primitiveVertices[primitiveIndex];
                if (primitiveSizes[primitiveIndex] == 3)
                {
                    triangle[0] = vertices.x;
                    triangle[1] = vertices.y;
                    triangle[2] = vertices.z;
                    EnsurePrimitiveAdded(ref detail, triangle);
                }
                else
                {
                    quad[0] = vertices.x;
                    quad[1] = vertices.y;
                    quad[2] = vertices.z;
                    quad[3] = vertices.w;
                    EnsurePrimitiveAdded(ref detail, quad);
                }
            }

            triangle.Dispose();
            quad.Dispose();
            primitiveSizes.Dispose();
            primitiveVertices.Dispose();

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
            primitiveCount = longitudeSegments * latitudeSegments;
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true)]
        private struct SpherePrimitivePolygonBuildJob : IJobParallelFor
        {
            public int LongitudeSegments;
            public int InteriorRingCount;
            public int RingVertexStride;

            public NativeArray<int4> PrimitiveVertices;
            public NativeArray<byte> PrimitiveSizes;

            public void Execute(int primitiveIndex)
            {
                const int northPoleVertex = 0;
                const int southPoleVertex = 1;

                int topCount = LongitudeSegments;
                int middleCount = (InteriorRingCount - 1) * LongitudeSegments;
                int middleStart = topCount;
                int middleEnd = middleStart + middleCount;

                if (primitiveIndex < topCount)
                {
                    int lon = primitiveIndex;
                    int current = 2 + lon;
                    int next = current + 1;
                    PrimitiveVertices[primitiveIndex] = new int4(northPoleVertex, next, current, 0);
                    PrimitiveSizes[primitiveIndex] = 3;
                    return;
                }

                if (primitiveIndex < middleEnd)
                {
                    int local = primitiveIndex - middleStart;
                    int ring = local / LongitudeSegments;
                    int lon = local - ring * LongitudeSegments;

                    int ringStart = 2 + ring * RingVertexStride;
                    int nextRingStart = ringStart + RingVertexStride;
                    int a = ringStart + lon;
                    int b = a + 1;
                    int c = nextRingStart + lon;
                    int d = c + 1;

                    PrimitiveVertices[primitiveIndex] = new int4(a, b, d, c);
                    PrimitiveSizes[primitiveIndex] = 4;
                    return;
                }

                int bottomLon = primitiveIndex - middleEnd;
                int lastRingStart = 2 + (InteriorRingCount - 1) * RingVertexStride;
                int currentBottom = lastRingStart + bottomLon;
                int nextBottom = currentBottom + 1;
                PrimitiveVertices[primitiveIndex] = new int4(southPoleVertex, currentBottom, nextBottom, 0);
                PrimitiveSizes[primitiveIndex] = 3;
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

        private static bool ShouldUseParallelBuild(int interiorRingCount, int primitiveCount)
        {
            return interiorRingCount >= ParallelRingThreshold && primitiveCount >= ParallelPrimitiveThreshold;
        }

        private static void EnsurePrimitiveAdded(ref NativeDetail detail, NativeArray<int> vertices)
        {
            int primitiveIndex = detail.AddPrimitive(vertices);
            ThrowIfPrimitiveAddFailed(primitiveIndex);
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void ThrowIfPrimitiveAddFailed(int primitiveIndex)
        {
            if (primitiveIndex < 0)
                throw new InvalidOperationException("Add primitive failed.");
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
