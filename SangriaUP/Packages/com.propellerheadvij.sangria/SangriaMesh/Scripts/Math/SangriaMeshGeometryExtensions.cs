using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    public static class SangriaMeshGeometryExtensions
    {
        private const float MinNormalLengthSq = 1e-12f;
        private const float MinPlaneDistanceTolerance = 1e-5f;
        private const float PlaneDistanceToleranceScale = 1e-5f;
        private const float MinCrossTolerance = 1e-7f;
        private const float CrossToleranceScale = 1e-7f;

        /// <summary>
        /// Returns true when polygon points define a planar convex polygon (in supplied winding order).
        /// </summary>
        public static bool IsPlanarConvexPolygon(this NativeArray<float3> polygonPoints)
        {
            int vertexCount = polygonPoints.Length;
            if (vertexCount < 3)
                return false;

            if (vertexCount == 3)
                return true;

            float3 normal = float3.zero;
            float3 boundsMin = polygonPoints[0];
            float3 boundsMax = boundsMin;

            for (int i = 0; i < vertexCount; i++)
            {
                float3 current = polygonPoints[i];
                float3 next = polygonPoints[(i + 1) % vertexCount];
                normal += math.cross(current, next);

                if (i > 0)
                {
                    boundsMin = MathExtensions.FastMin(boundsMin, current);
                    boundsMax = MathExtensions.FastMax(boundsMax, current);
                }
            }

            float normalLengthSq = math.lengthsq(normal);
            if (normalLengthSq <= MinNormalLengthSq)
                return false;

            float extent = math.cmax(boundsMax - boundsMin);
            float planeDistanceTolerance = MathExtensions.FastMax(
                MinPlaneDistanceTolerance,
                extent * PlaneDistanceToleranceScale);

            float invNormalLength = math.rsqrt(normalLengthSq);
            float3 unitNormal = normal * invNormalLength;
            float planeDistance = math.dot(unitNormal, polygonPoints[0]);

            for (int i = 1; i < vertexCount; i++)
            {
                float signedDistance = math.dot(unitNormal, polygonPoints[i]) - planeDistance;
                if (math.abs(signedDistance) > planeDistanceTolerance)
                    return false;
            }

            int droppedAxis = DominantAxis(math.abs(normal));
            float windingSign = 0f;
            float crossTolerance = MathExtensions.FastMax(
                MinCrossTolerance,
                extent * CrossToleranceScale);

            for (int i = 0; i < vertexCount; i++)
            {
                float2 prev = ProjectTo2D(polygonPoints[(i + vertexCount - 1) % vertexCount], droppedAxis);
                float2 curr = ProjectTo2D(polygonPoints[i], droppedAxis);
                float2 next = ProjectTo2D(polygonPoints[(i + 1) % vertexCount], droppedAxis);

                float2 edgeA = curr - prev;
                float2 edgeB = next - curr;
                float cross = edgeA.x * edgeB.y - edgeA.y * edgeB.x;

                if (math.abs(cross) <= crossTolerance)
                    continue;

                float currentSign = cross > 0f ? 1f : -1f;
                if (windingSign == 0f)
                {
                    windingSign = currentSign;
                    continue;
                }

                if (currentSign != windingSign)
                    return false;
            }

            return windingSign != 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int DominantAxis(in float3 v)
        {
            if (v.x >= v.y && v.x >= v.z)
                return 0;
            return v.y >= v.z ? 1 : 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float2 ProjectTo2D(in float3 p, int droppedAxis)
        {
            if (droppedAxis == 0)
                return new float2(p.y, p.z);
            if (droppedAxis == 1)
                return new float2(p.x, p.z);
            return new float2(p.x, p.y);
        }
    }
}
