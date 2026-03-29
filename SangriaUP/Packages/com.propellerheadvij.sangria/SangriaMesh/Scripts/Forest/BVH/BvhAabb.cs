using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SangriaMesh
{
    public struct BvhAabb
    {
        public float3 Min;
        public float3 Max;

        public float3 Center => (Min + Max) * 0.5f;

        public float3 Extents => (Max - Min) * 0.5f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float SurfaceArea()
        {
            float3 size = MathExtensions.FastMax(Max - Min, new float3(0f));
            return 2f * (size.x * size.y + size.y * size.z + size.z * size.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(BvhAabb other)
        {
            return Min.x <= other.Max.x && Max.x >= other.Min.x &&
                   Min.y <= other.Max.y && Max.y >= other.Min.y &&
                   Min.z <= other.Max.z && Max.z >= other.Min.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(BvhAabb other)
        {
            return Min.x <= other.Min.x && Max.x >= other.Max.x &&
                   Min.y <= other.Min.y && Max.y >= other.Max.y &&
                   Min.z <= other.Min.z && Max.z >= other.Max.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsRay(float3 rayOrigin, float3 invDir, float tNear, float tFar)
        {
            float3 t0 = (Min - rayOrigin) * invDir;
            float3 t1 = (Max - rayOrigin) * invDir;
            float3 tmin = MathExtensions.FastMin(t0, t1);
            float3 tmax = MathExtensions.FastMax(t0, t1);

            float enter = MathExtensions.FastMax(
                tNear,
                MathExtensions.FastMax(tmin.x, MathExtensions.FastMax(tmin.y, tmin.z)));

            float exit = MathExtensions.FastMin(
                tFar,
                MathExtensions.FastMin(tmax.x, MathExtensions.FastMin(tmax.y, tmax.z)));

            return enter <= exit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(float3 point)
        {
            return point.x >= Min.x && point.x <= Max.x &&
                   point.y >= Min.y && point.y <= Max.y &&
                   point.z >= Min.z && point.z <= Max.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BvhAabb Expanded(float value)
        {
            float3 expand = new float3(value);
            return new BvhAabb
            {
                Min = Min - expand,
                Max = Max + expand
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BvhAabb FromCenterExtents(float3 center, float3 extents)
        {
            return new BvhAabb
            {
                Min = center - extents,
                Max = center + extents
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BvhAabb Union(BvhAabb a, BvhAabb b)
        {
            return new BvhAabb
            {
                Min = MathExtensions.FastMin(a.Min, b.Min),
                Max = MathExtensions.FastMax(a.Max, b.Max)
            };
        }
    }
}
