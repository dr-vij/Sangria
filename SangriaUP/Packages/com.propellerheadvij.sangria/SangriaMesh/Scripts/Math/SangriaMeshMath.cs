using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace SangriaMesh
{
    public static class MathExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastMin(float lhs, float rhs)
        {
            return lhs < rhs ? lhs : rhs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FastMax(float lhs, float rhs)
        {
            return lhs > rhs ? lhs : rhs;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 FastMin(float3 lhs, float3 rhs)
        {
            return new float3(
                FastMin(lhs.x, rhs.x),
                FastMin(lhs.y, rhs.y),
                FastMin(lhs.z, rhs.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 FastMax(float3 lhs, float3 rhs)
        {
            return new float3(
                FastMax(lhs.x, rhs.x),
                FastMax(lhs.y, rhs.y),
                FastMax(lhs.z, rhs.z));
        }
    }
}
