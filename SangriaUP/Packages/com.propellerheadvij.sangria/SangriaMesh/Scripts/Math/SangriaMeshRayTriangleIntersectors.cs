using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Mathematics;

namespace SangriaMesh
{
    public struct RayTriangleHit
    {
        public float T;
        public float U;
        public float V;
        public float Determinant;
        public float3 GeometricNormal;

        public readonly float W => 1f - U - V;
    }

    /// <summary>
    /// Ray/triangle intersectors adapted from Embree:
    /// - Moeller: https://github.com/RenderKit/embree/blob/f590db83ef6559387df7f6d8725c34fb7acf851d/kernels/geometry/triangle_intersector_moeller.h
    /// - Pluecker: https://github.com/RenderKit/embree/blob/f590db83ef6559387df7f6d8725c34fb7acf851d/kernels/geometry/triangle_intersector_pluecker.h
    /// - Woop: https://github.com/RenderKit/embree/blob/f590db83ef6559387df7f6d8725c34fb7acf851d/kernels/geometry/triangle_intersector_woop.h
    /// </summary>
    [BurstCompile]
    public static class SangriaMeshRayTriangleIntersectors
    {
        public const float DefaultEpsilon = 1e-8f;
        private const float FloatUlp = 1.1920929e-7f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryIntersectMoeller(
            in float3 rayOrigin,
            in float3 rayDirection,
            float tNear,
            float tFar,
            in float3 v0,
            in float3 v1,
            in float3 v2,
            out RayTriangleHit hit,
            bool backfaceCulling = false,
            float epsilon = DefaultEpsilon)
        {
            hit = default;

            float3 e1 = v0 - v1;
            float3 e2 = v2 - v0;
            float3 triNg = math.cross(e2, e1);

            float3 c = v0 - rayOrigin;
            float3 r = math.cross(c, rayDirection);

            float den = math.dot(triNg, rayDirection);
            float absDen = math.abs(den);
            if (absDen <= epsilon)
                return false;

            if (backfaceCulling && den >= -epsilon)
                return false;

            float sgnDen = den < 0f ? -1f : 1f;
            float uScaled = math.dot(r, e2) * sgnDen;
            float vScaled = math.dot(r, e1) * sgnDen;

            if (uScaled < 0f || vScaled < 0f || uScaled + vScaled > absDen)
                return false;

            float tScaled = math.dot(triNg, c) * sgnDen;
            if (absDen * tNear >= tScaled || tScaled > absDen * tFar)
                return false;

            float invAbsDen = 1f / absDen;
            hit.T = tScaled * invAbsDen;
            hit.U = uScaled * invAbsDen;
            hit.V = vScaled * invAbsDen;
            hit.Determinant = den;
            hit.GeometricNormal = triNg;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryIntersectPluecker(
            in float3 rayOrigin,
            in float3 rayDirection,
            float tNear,
            float tFar,
            in float3 v0,
            in float3 v1,
            in float3 v2,
            out RayTriangleHit hit,
            bool backfaceCulling = false,
            float epsilon = DefaultEpsilon)
        {
            hit = default;

            float3 rv0 = v0 - rayOrigin;
            float3 rv1 = v1 - rayOrigin;
            float3 rv2 = v2 - rayOrigin;

            float3 e0 = rv2 - rv0;
            float3 e1 = rv0 - rv1;
            float3 e2 = rv1 - rv2;

            float U = math.dot(math.cross(e0, rv2 + rv0), rayDirection);
            float V = math.dot(math.cross(e1, rv0 + rv1), rayDirection);
            float W = math.dot(math.cross(e2, rv1 + rv2), rayDirection);

            float uvw = U + V + W;
            float edgeEps = FloatUlp * math.abs(uvw);
            float minUVW = MathExtensions.FastMin(U, MathExtensions.FastMin(V, W));
            float maxUVW = MathExtensions.FastMax(U, MathExtensions.FastMax(V, W));
            bool edgePass = backfaceCulling
                ? maxUVW <= edgeEps
                : (minUVW >= -edgeEps || maxUVW <= edgeEps);

            if (!edgePass)
                return false;

            float3 triNg = math.cross(v2 - v0, v0 - v1);
            float den = 2f * math.dot(triNg, rayDirection);
            if (math.abs(den) <= epsilon)
                return false;

            if (backfaceCulling && den >= -epsilon)
                return false;

            float t = (2f * math.dot(rv0, triNg)) / den;
            if (t < tNear || t > tFar)
                return false;

            if (math.abs(uvw) <= epsilon)
                return false;

            float invUVW = 1f / uvw;
            hit.T = t;
            hit.U = MathExtensions.FastMin(U * invUVW, 1f);
            hit.V = MathExtensions.FastMin(V * invUVW, 1f);
            hit.Determinant = den;
            hit.GeometricNormal = triNg;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryIntersectWoop(
            in float3 rayOrigin,
            in float3 rayDirection,
            float tNear,
            float tFar,
            in float3 v0,
            in float3 v1,
            in float3 v2,
            out RayTriangleHit hit,
            bool backfaceCulling = false,
            float epsilon = DefaultEpsilon)
        {
            hit = default;

            int kz = MaxAbsDimension(math.abs(rayDirection));
            float dirKz = rayDirection[kz];
            if (math.abs(dirKz) <= epsilon)
                return false;

            int kx = (kz + 1) % 3;
            int ky = (kx + 1) % 3;
            if (dirKz < 0f)
                (kx, ky) = (ky, kx);

            float invDirKz = 1f / dirKz;
            float3 shear = new float3(
                rayDirection[kx] * invDirKz,
                rayDirection[ky] * invDirKz,
                invDirKz);

            float3 org = new float3(rayOrigin[kx], rayOrigin[ky], rayOrigin[kz]);
            float3 A = new float3(v0[kx], v0[ky], v0[kz]) - org;
            float3 B = new float3(v1[kx], v1[ky], v1[kz]) - org;
            float3 C = new float3(v2[kx], v2[ky], v2[kz]) - org;

            float Ax = A.x - A.z * shear.x;
            float Ay = A.y - A.z * shear.y;
            float Bx = B.x - B.z * shear.x;
            float By = B.y - B.z * shear.y;
            float Cx = C.x - C.z * shear.x;
            float Cy = C.y - C.z * shear.y;

            float u0 = Cx * By;
            float u1 = Cy * Bx;
            float v0Scaled = Ax * Cy;
            float v1Scaled = Ay * Cx;
            float w0 = Bx * Ay;
            float w1 = By * Ax;

            bool sameSign =
                (u0 >= u1 && v0Scaled >= v1Scaled && w0 >= w1) ||
                (u0 <= u1 && v0Scaled <= v1Scaled && w0 <= w1);
            if (!sameSign)
                return false;

            float U = u0 - u1;
            float V = v0Scaled - v1Scaled;
            float W = w0 - w1;
            float det = U + V + W;
            if (math.abs(det) <= epsilon)
                return false;

            float3 triNg = math.cross(v2 - v0, v0 - v1);
            if (backfaceCulling && math.dot(triNg, rayDirection) >= -epsilon)
                return false;

            float invDet = 1f / det;
            float T = U * (shear.z * A.z) + V * (shear.z * B.z) + W * (shear.z * C.z);
            float t = T * invDet;
            if (!(tNear < t && t <= tFar))
                return false;

            hit.T = t;
            hit.U = U * invDet;
            hit.V = V * invDet;
            hit.Determinant = det;
            hit.GeometricNormal = triNg;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int MaxAbsDimension(in float3 v)
        {
            if (v.x >= v.y && v.x >= v.z)
                return 0;
            return v.y >= v.z ? 1 : 2;
        }

    }
}
