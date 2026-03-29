using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace SangriaMesh
{
    /// <summary>
    /// Triangle/triangle intersector adapted from Embree:
    /// https://github.com/RenderKit/embree/blob/f590db83ef6559387df7f6d8725c34fb7acf851d/kernels/geometry/triangle_triangle_intersector.h
    /// </summary>
    [BurstCompile]
    public static class SangriaMeshTriangleTriangleIntersector
    {
        public const float DefaultEpsilon = 1e-5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Intersects(
            in float3 a0,
            in float3 a1,
            in float3 a2,
            in float3 b0,
            in float3 b1,
            in float3 b2,
            float epsilon = DefaultEpsilon)
        {
            float3 Na = math.cross(a1 - a0, a2 - a0);
            float Ca = math.dot(Na, a0);
            float3 Nb = math.cross(b1 - b0, b2 - b0);
            float Cb = math.dot(Nb, b0);

            float da0 = math.dot(Nb, a0) - Cb;
            float da1 = math.dot(Nb, a1) - Cb;
            float da2 = math.dot(Nb, a2) - Cb;
            if (Max3(da0, da1, da2) < -epsilon)
                return false;
            if (Min3(da0, da1, da2) > epsilon)
                return false;

            float db0 = math.dot(Na, b0) - Ca;
            float db1 = math.dot(Na, b1) - Ca;
            float db2 = math.dot(Na, b2) - Ca;
            if (Max3(db0, db1, db2) < -epsilon)
                return false;
            if (Min3(db0, db1, db2) > epsilon)
                return false;

            bool coplanarAInBPlane = math.abs(da0) < epsilon && math.abs(da1) < epsilon && math.abs(da2) < epsilon;
            bool coplanarBInAPlane = math.abs(db0) < epsilon && math.abs(db1) < epsilon && math.abs(db2) < epsilon;
            if (coplanarAInBPlane || coplanarBInAPlane)
            {
                int dz = MaxAbsDimension(Na);
                int dx = (dz + 1) % 3;
                int dy = (dx + 1) % 3;

                float2 A0 = new float2(a0[dx], a0[dy]);
                float2 A1 = new float2(a1[dx], a1[dy]);
                float2 A2 = new float2(a2[dx], a2[dy]);
                float2 B0 = new float2(b0[dx], b0[dy]);
                float2 B1 = new float2(b1[dx], b1[dy]);
                float2 B2 = new float2(b2[dx], b2[dy]);

                return IntersectsCoplanar(A0, A1, A2, B0, B1, B2);
            }

            float3 D = math.cross(Na, Nb);
            float pa0 = math.dot(D, a0);
            float pa1 = math.dot(D, a1);
            float pa2 = math.dot(D, a2);
            float pb0 = math.dot(D, b0);
            float pb1 = math.dot(D, b1);
            float pb2 = math.dot(D, b2);

            bool hasBa = false;
            float baMin = 0f;
            float baMax = 0f;
            ExtendCrossingInterval(ref hasBa, ref baMin, ref baMax, pa0, pa1, da0, da1);
            ExtendCrossingInterval(ref hasBa, ref baMin, ref baMax, pa1, pa2, da1, da2);
            ExtendCrossingInterval(ref hasBa, ref baMin, ref baMax, pa2, pa0, da2, da0);

            bool hasBb = false;
            float bbMin = 0f;
            float bbMax = 0f;
            ExtendCrossingInterval(ref hasBb, ref bbMin, ref bbMax, pb0, pb1, db0, db1);
            ExtendCrossingInterval(ref hasBb, ref bbMin, ref bbMax, pb1, pb2, db1, db2);
            ExtendCrossingInterval(ref hasBb, ref bbMin, ref bbMax, pb2, pb0, db2, db0);

            return hasBa && hasBb && baMin <= bbMax && baMax >= bbMin;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IntersectsCoplanar(in float2 a0, in float2 a1, in float2 a2, in float2 b0, in float2 b1, in float2 b2)
        {
            if (IntersectsLineLine(a0, a1, b0, b1)) return true;
            if (IntersectsLineLine(a0, a1, b1, b2)) return true;
            if (IntersectsLineLine(a0, a1, b2, b0)) return true;

            if (IntersectsLineLine(a1, a2, b0, b1)) return true;
            if (IntersectsLineLine(a1, a2, b1, b2)) return true;
            if (IntersectsLineLine(a1, a2, b2, b0)) return true;

            if (IntersectsLineLine(a2, a0, b0, b1)) return true;
            if (IntersectsLineLine(a2, a0, b1, b2)) return true;
            if (IntersectsLineLine(a2, a0, b2, b0)) return true;

            bool aInB =
                PointInsideTriangle(a0, b0, b1, b2) &&
                PointInsideTriangle(a1, b0, b1, b2) &&
                PointInsideTriangle(a2, b0, b1, b2);
            if (aInB)
                return true;

            bool bInA =
                PointInsideTriangle(b0, a0, a1, a2) &&
                PointInsideTriangle(b1, a0, a1, a2) &&
                PointInsideTriangle(b2, a0, a1, a2);
            return bInA;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool PointInsideTriangle(in float2 p, in float2 a, in float2 b, in float2 c)
        {
            bool pab = PointLineSide(p, a, b);
            bool pbc = PointLineSide(p, b, c);
            bool pca = PointLineSide(p, c, a);
            return pab == pbc && pab == pca;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IntersectsLineLine(in float2 a0, in float2 a1, in float2 b0, in float2 b1)
        {
            bool differentSides0 = PointLineSide(b0, a0, a1) != PointLineSide(b1, a0, a1);
            bool differentSides1 = PointLineSide(a0, b0, b1) != PointLineSide(a1, b0, b1);
            return differentSides0 && differentSides1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool PointLineSide(in float2 p, in float2 a0, in float2 a1)
        {
            return Det2(p - a0, a0 - a1) >= 0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Det2(in float2 a, in float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExtendCrossingInterval(
            ref bool hasInterval,
            ref float intervalMin,
            ref float intervalMax,
            float p0,
            float p1,
            float d0,
            float d1)
        {
            if (MathExtensions.FastMin(d0, d1) <= 0f &&
                MathExtensions.FastMax(d0, d1) >= 0f &&
                math.abs(d0 - d1) > 0f)
            {
                float t = InterpolateAtPlaneCrossing(p0, p1, d0, d1);
                if (!hasInterval)
                {
                    intervalMin = t;
                    intervalMax = t;
                    hasInterval = true;
                }
                else
                {
                    intervalMin = MathExtensions.FastMin(intervalMin, t);
                    intervalMax = MathExtensions.FastMax(intervalMax, t);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float InterpolateAtPlaneCrossing(float p0, float p1, float d0, float d1)
        {
            return p0 + (p1 - p0) * d0 / (d0 - d1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MaxAbsDimension(in float3 v)
        {
            float3 av = math.abs(v);
            if (av.x >= av.y && av.x >= av.z)
                return 0;
            return av.y >= av.z ? 1 : 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Max3(float a, float b, float c)
        {
            return MathExtensions.FastMax(a, MathExtensions.FastMax(b, c));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Min3(float a, float b, float c)
        {
            return MathExtensions.FastMin(a, MathExtensions.FastMin(b, c));
        }
    }
}
