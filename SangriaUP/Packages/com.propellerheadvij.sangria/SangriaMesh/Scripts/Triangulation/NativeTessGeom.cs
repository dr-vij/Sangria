using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh.NativeTess
{
    [BurstCompile]
    internal static class Geom
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsWindingInside(WindingRule rule, int n)
        {
            switch (rule)
            {
                case WindingRule.EvenOdd: return (n & 1) == 1;
                case WindingRule.NonZero: return n != 0;
                case WindingRule.Positive: return n > 0;
                case WindingRule.Negative: return n < 0;
                case WindingRule.AbsGeqTwo: return n >= 2 || n <= -2;
                default: return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool VertCCW(in SweepVertex u, in SweepVertex v, in SweepVertex w)
        {
            return (u.s * (v.t - w.t) + v.s * (w.t - u.t) + w.s * (u.t - v.t)) >= 0.0f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool VertEq(in SweepVertex lhs, in SweepVertex rhs)
        {
            return lhs.s == rhs.s && lhs.t == rhs.t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool VertLeq(in SweepVertex lhs, in SweepVertex rhs)
        {
            return (lhs.s < rhs.s) || (lhs.s == rhs.s && lhs.t <= rhs.t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EdgeEval(in SweepVertex u, in SweepVertex v, in SweepVertex w)
        {
            float gapL = v.s - u.s;
            float gapR = w.s - v.s;
            if (gapL + gapR > 0.0f)
            {
                if (gapL < gapR)
                    return (v.t - u.t) + (u.t - w.t) * (gapL / (gapL + gapR));
                else
                    return (v.t - w.t) + (w.t - u.t) * (gapR / (gapL + gapR));
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float EdgeSign(in SweepVertex u, in SweepVertex v, in SweepVertex w)
        {
            float gapL = v.s - u.s;
            float gapR = w.s - v.s;
            if (gapL + gapR > 0.0f)
                return (v.t - w.t) * gapL + (v.t - u.t) * gapR;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TransLeq(in SweepVertex lhs, in SweepVertex rhs)
        {
            return (lhs.t < rhs.t) || (lhs.t == rhs.t && lhs.s <= rhs.s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float TransEval(in SweepVertex u, in SweepVertex v, in SweepVertex w)
        {
            float gapL = v.t - u.t;
            float gapR = w.t - v.t;
            if (gapL + gapR > 0.0f)
            {
                if (gapL < gapR)
                    return (v.s - u.s) + (u.s - w.s) * (gapL / (gapL + gapR));
                else
                    return (v.s - w.s) + (w.s - u.s) * (gapR / (gapL + gapR));
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float TransSign(in SweepVertex u, in SweepVertex v, in SweepVertex w)
        {
            float gapL = v.t - u.t;
            float gapR = w.t - v.t;
            if (gapL + gapR > 0.0f)
                return (v.s - w.s) * gapL + (v.s - u.s) * gapR;
            return 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float VertL1dist(in SweepVertex u, in SweepVertex v)
        {
            return math.abs(u.s - v.s) + math.abs(u.t - v.t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Interpolate(float a, float x, float b, float y)
        {
            if (a < 0.0f) a = 0.0f;
            if (b < 0.0f) b = 0.0f;
            return ((a <= b) ? ((b == 0.0f) ? ((x + y) / 2.0f)
                : (x + (y - x) * (a / (a + b))))
                : (y + (x - y) * (b / (a + b))));
        }

        public static void EdgeIntersect(
            in SweepVertex o1In, in SweepVertex d1In,
            in SweepVertex o2In, in SweepVertex d2In,
            ref SweepVertex v)
        {
            int io1 = 0, id1 = 1, io2 = 2, id2 = 3;
            unsafe
            {
                SweepVertex* arr = stackalloc SweepVertex[4];
                arr[0] = o1In; arr[1] = d1In; arr[2] = o2In; arr[3] = d2In;

                if (!VertLeq(arr[io1], arr[id1])) { int tmp = io1; io1 = id1; id1 = tmp; }
                if (!VertLeq(arr[io2], arr[id2])) { int tmp = io2; io2 = id2; id2 = tmp; }
                if (!VertLeq(arr[io1], arr[io2])) { int tmp = io1; io1 = io2; io2 = tmp; tmp = id1; id1 = id2; id2 = tmp; }

                if (!VertLeq(arr[io2], arr[id1]))
                {
                    v.s = (arr[io2].s + arr[id1].s) / 2.0f;
                }
                else if (VertLeq(arr[id1], arr[id2]))
                {
                    float z1 = EdgeEval(arr[io1], arr[io2], arr[id1]);
                    float z2 = EdgeEval(arr[io2], arr[id1], arr[id2]);
                    if (z1 + z2 < 0.0f) { z1 = -z1; z2 = -z2; }
                    v.s = Interpolate(z1, arr[io2].s, z2, arr[id1].s);
                }
                else
                {
                    float z1 = EdgeSign(arr[io1], arr[io2], arr[id1]);
                    float z2 = -EdgeSign(arr[io1], arr[id2], arr[id1]);
                    if (z1 + z2 < 0.0f) { z1 = -z1; z2 = -z2; }
                    v.s = Interpolate(z1, arr[io2].s, z2, arr[id2].s);
                }

                if (!TransLeq(arr[io1], arr[id1])) { int tmp = io1; io1 = id1; id1 = tmp; }
                if (!TransLeq(arr[io2], arr[id2])) { int tmp = io2; io2 = id2; id2 = tmp; }
                if (!TransLeq(arr[io1], arr[io2])) { int tmp = io1; io1 = io2; io2 = tmp; tmp = id1; id1 = id2; id2 = tmp; }

                if (!TransLeq(arr[io2], arr[id1]))
                {
                    v.t = (arr[io2].t + arr[id1].t) / 2.0f;
                }
                else if (TransLeq(arr[id1], arr[id2]))
                {
                    float z1 = TransEval(arr[io1], arr[io2], arr[id1]);
                    float z2 = TransEval(arr[io2], arr[id1], arr[id2]);
                    if (z1 + z2 < 0.0f) { z1 = -z1; z2 = -z2; }
                    v.t = Interpolate(z1, arr[io2].t, z2, arr[id1].t);
                }
                else
                {
                    float z1 = TransSign(arr[io1], arr[io2], arr[id1]);
                    float z2 = -TransSign(arr[io1], arr[id2], arr[id1]);
                    if (z1 + z2 < 0.0f) { z1 = -z1; z2 = -z2; }
                    v.t = Interpolate(z1, arr[io2].t, z2, arr[id2].t);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float FaceArea(ref TessMesh mesh, int faceIdx)
        {
            float area = 0;
            int startEdge = mesh.faces[faceIdx].anEdge;
            int e = startEdge;
            do
            {
                ref var eOrg = ref mesh.vertices.ElementAt(mesh.edges[e].org);
                int dstIdx = mesh.edges[mesh.edges[e].sym].org;
                ref var eDst = ref mesh.vertices.ElementAt(dstIdx);
                area += (eOrg.s - eDst.s) * (eOrg.t + eDst.t);
                e = mesh.edges[e].lnext;
            } while (e != startEdge);
            return area;
        }
    }
}
