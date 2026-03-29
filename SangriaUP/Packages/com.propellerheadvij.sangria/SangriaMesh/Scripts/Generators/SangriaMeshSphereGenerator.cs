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

            var primitiveVertices = new NativeArray<int4>(primitiveCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var primitiveSizes = new NativeArray<byte>(primitiveCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            unsafe
            {
                float3* pointPositionPtr = pointPositionAccessor.GetBasePointer();
                float3* pointNormalPtr = pointNormalAccessor.GetBasePointer();
                float3* vertexNormalPtr = vertexNormalAccessor.GetBasePointer();
                float2* vertexUvPtr = vertexUvAccessor.GetBasePointer();
                int* vertexToPointPtr = detail.GetVertexToPointPointerUnchecked();
                InitializePolePoints(radius, pointPositionPtr, pointNormalPtr);

                var ringPointJob = new SphereInteriorRingPointBuildJob
                {
                    Radius = radius,
                    LongitudeSegments = longitudeSegments,
                    LatitudeSegments = latitudeSegments,
                    PointPositionPtr = pointPositionPtr,
                    PointNormalPtr = pointNormalPtr
                };

                if (ShouldUseParallelBuild(interiorRingCount, primitiveCount))
                    ringPointJob.Schedule(interiorRingCount, 1).Complete();
                else
                    ringPointJob.Run(interiorRingCount);

                var vertexBuildJob = new SpherePrimitiveVertexBuildJob
                {
                    LongitudeSegments = longitudeSegments,
                    InteriorRingCount = interiorRingCount,
                    LatitudeSegments = latitudeSegments,
                    PointNormalPtr = pointNormalPtr,
                    VertexNormalPtr = vertexNormalPtr,
                    VertexUvPtr = vertexUvPtr,
                    VertexToPointPtr = vertexToPointPtr,
                    PrimitiveVertices = primitiveVertices,
                    PrimitiveSizes = primitiveSizes
                };

                if (ShouldUseParallelBuild(interiorRingCount, primitiveCount))
                    vertexBuildJob.Schedule(primitiveCount, 128).Complete();
                else
                    vertexBuildJob.Run(primitiveCount);
            }

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
            primitiveCount = longitudeSegments * latitudeSegments;
            int topCapVertices = longitudeSegments * 3;
            int middleVertices = (interiorRingCount - 1) * longitudeSegments * 4;
            int bottomCapVertices = longitudeSegments * 3;
            vertexCount = topCapVertices + middleVertices + bottomCapVertices;
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true)]
        private unsafe struct SphereInteriorRingPointBuildJob : IJobParallelFor
        {
            public float Radius;
            public int LongitudeSegments;
            public int LatitudeSegments;

            [NativeDisableUnsafePtrRestriction] public float3* PointPositionPtr;
            [NativeDisableUnsafePtrRestriction] public float3* PointNormalPtr;

            public void Execute(int ring)
            {
                float v = (float)(ring + 1) / LatitudeSegments;
                float phi = v * math.PI;
                math.sincos(phi, out float sinPhi, out float cosPhi);

                int pointRingStart = 2 + ring * LongitudeSegments;
                float deltaTheta = 2f * math.PI / LongitudeSegments;
                math.sincos(deltaTheta, out float sinDelta, out float cosDelta);

                float s = 0f;
                float c = 1f;

                for (int lon = 0; lon < LongitudeSegments; lon++)
                {
                    int pointIndex = pointRingStart + lon;
                    float3 normal = new float3(c * sinPhi, cosPhi, s * sinPhi);
                    PointPositionPtr[pointIndex] = normal * Radius;
                    PointNormalPtr[pointIndex] = normal;

                    float nextS = s * cosDelta + c * sinDelta;
                    float nextC = c * cosDelta - s * sinDelta;
                    s = nextS;
                    c = nextC;
                }
            }
        }

        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low, CompileSynchronously = true)]
        private unsafe struct SpherePrimitiveVertexBuildJob : IJobParallelFor
        {
            public int LongitudeSegments;
            public int InteriorRingCount;
            public int LatitudeSegments;

            [NativeDisableUnsafePtrRestriction] [ReadOnly] public float3* PointNormalPtr;
            [NativeDisableUnsafePtrRestriction] public float3* VertexNormalPtr;
            [NativeDisableUnsafePtrRestriction] public float2* VertexUvPtr;
            [NativeDisableUnsafePtrRestriction] public int* VertexToPointPtr;

            public NativeArray<int4> PrimitiveVertices;
            public NativeArray<byte> PrimitiveSizes;

            public void Execute(int primitiveIndex)
            {
                const int northPolePoint = 0;
                const int southPolePoint = 1;

                int topCount = LongitudeSegments;
                int middleCount = (InteriorRingCount - 1) * LongitudeSegments;
                int middleStart = topCount;
                int middleEnd = middleStart + middleCount;
                int topVertexTotal = topCount * 3;
                int middleVertexTotal = middleCount * 4;
                float invLon = 1f / LongitudeSegments;

                if (primitiveIndex < topCount)
                {
                    int lon = primitiveIndex;
                    int nextLon = (lon + 1) % LongitudeSegments;
                    int vBase = lon * 3;

                    int pointCurrent = 2 + lon;
                    int pointNext = 2 + nextLon;

                    float v = 1f / LatitudeSegments;

                    VertexToPointPtr[vBase] = northPolePoint;
                    VertexNormalPtr[vBase] = PointNormalPtr[northPolePoint];
                    VertexUvPtr[vBase] = new float2((lon + 0.5f) * invLon, 0f);

                    VertexToPointPtr[vBase + 1] = pointNext;
                    VertexNormalPtr[vBase + 1] = PointNormalPtr[pointNext];
                    VertexUvPtr[vBase + 1] = new float2((lon + 1) * invLon, v);

                    VertexToPointPtr[vBase + 2] = pointCurrent;
                    VertexNormalPtr[vBase + 2] = PointNormalPtr[pointCurrent];
                    VertexUvPtr[vBase + 2] = new float2(lon * invLon, v);

                    PrimitiveVertices[primitiveIndex] = new int4(vBase, vBase + 1, vBase + 2, 0);
                    PrimitiveSizes[primitiveIndex] = 3;
                    return;
                }

                if (primitiveIndex < middleEnd)
                {
                    int local = primitiveIndex - middleStart;
                    int ring = local / LongitudeSegments;
                    int lon = local - ring * LongitudeSegments;
                    int nextLon = (lon + 1) % LongitudeSegments;
                    int vBase = topVertexTotal + local * 4;

                    int ringPointStart = 2 + ring * LongitudeSegments;
                    int nextRingPointStart = 2 + (ring + 1) * LongitudeSegments;

                    int pA = ringPointStart + lon;
                    int pB = ringPointStart + nextLon;
                    int pC = nextRingPointStart + nextLon;
                    int pD = nextRingPointStart + lon;

                    float vTop = (float)(ring + 1) / LatitudeSegments;
                    float vBot = (float)(ring + 2) / LatitudeSegments;
                    float uLeft = lon * invLon;
                    float uRight = (lon + 1) * invLon;

                    VertexToPointPtr[vBase] = pA;
                    VertexNormalPtr[vBase] = PointNormalPtr[pA];
                    VertexUvPtr[vBase] = new float2(uLeft, vTop);

                    VertexToPointPtr[vBase + 1] = pB;
                    VertexNormalPtr[vBase + 1] = PointNormalPtr[pB];
                    VertexUvPtr[vBase + 1] = new float2(uRight, vTop);

                    VertexToPointPtr[vBase + 2] = pC;
                    VertexNormalPtr[vBase + 2] = PointNormalPtr[pC];
                    VertexUvPtr[vBase + 2] = new float2(uRight, vBot);

                    VertexToPointPtr[vBase + 3] = pD;
                    VertexNormalPtr[vBase + 3] = PointNormalPtr[pD];
                    VertexUvPtr[vBase + 3] = new float2(uLeft, vBot);

                    PrimitiveVertices[primitiveIndex] = new int4(vBase, vBase + 1, vBase + 2, vBase + 3);
                    PrimitiveSizes[primitiveIndex] = 4;
                    return;
                }

                int bLon = primitiveIndex - middleEnd;
                int bNextLon = (bLon + 1) % LongitudeSegments;
                int bBase = topVertexTotal + middleVertexTotal + bLon * 3;

                int lastRingPointStart = 2 + (InteriorRingCount - 1) * LongitudeSegments;
                int pCurBot = lastRingPointStart + bLon;
                int pNxtBot = lastRingPointStart + bNextLon;

                float vLast = (float)(LatitudeSegments - 1) / LatitudeSegments;

                VertexToPointPtr[bBase] = southPolePoint;
                VertexNormalPtr[bBase] = PointNormalPtr[southPolePoint];
                VertexUvPtr[bBase] = new float2((bLon + 0.5f) * invLon, 1f);

                VertexToPointPtr[bBase + 1] = pCurBot;
                VertexNormalPtr[bBase + 1] = PointNormalPtr[pCurBot];
                VertexUvPtr[bBase + 1] = new float2(bLon * invLon, vLast);

                VertexToPointPtr[bBase + 2] = pNxtBot;
                VertexNormalPtr[bBase + 2] = PointNormalPtr[pNxtBot];
                VertexUvPtr[bBase + 2] = new float2((bLon + 1) * invLon, vLast);

                PrimitiveVertices[primitiveIndex] = new int4(bBase, bBase + 1, bBase + 2, 0);
                PrimitiveSizes[primitiveIndex] = 3;
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

        private static unsafe void InitializePolePoints(
            float radius,
            float3* pointPositionPtr,
            float3* pointNormalPtr)
        {
            const int northPolePoint = 0;
            const int southPolePoint = 1;

            pointPositionPtr[northPolePoint] = new float3(0f, radius, 0f);
            pointPositionPtr[southPolePoint] = new float3(0f, -radius, 0f);
            pointNormalPtr[northPolePoint] = new float3(0f, 1f, 0f);
            pointNormalPtr[southPolePoint] = new float3(0f, -1f, 0f);
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
