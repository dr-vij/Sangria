using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh.NativeTess
{
    internal enum WindingRule : byte
    {
        EvenOdd = 0,
        NonZero = 1,
        Positive = 2,
        Negative = 3,
        AbsGeqTwo = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SweepVertex
    {
        public float3 coords;
        public float s, t;
        public int anEdge;
        public PQHandle pqHandle;
        public int n;
        public int prev, next;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SweepFace
    {
        public int anEdge;
        public int prev, next;
        public int trail;
        public int n;
        public bool marked;
        public bool inside;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct SweepHalfEdge
    {
        public int sym;
        public int onext;
        public int lnext;
        public int org;
        public int lface;
        public int activeRegion;
        public int winding;
        public int next;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ActiveRegion
    {
        public int eUp;
        public int nodeUp;
        public int windingNumber;
        public bool inside;
        public bool sentinel;
        public bool dirty;
        public bool fixUpperEdge;
    }

    internal struct PQHandle
    {
        public const int Invalid = 0x0fffffff;
        public int handle;
    }

    internal struct FreeList
    {
        private int m_FirstFree;

        public FreeList(int unused) { m_FirstFree = -1; }

        public bool HasFree => m_FirstFree >= 0;

        public int Pop(ref UnsafeList<int> freeSlots)
        {
            int idx = m_FirstFree;
            m_FirstFree = freeSlots[idx];
            return idx;
        }

        public void Push(ref UnsafeList<int> freeSlots, int index)
        {
            freeSlots[index] = m_FirstFree;
            m_FirstFree = index;
        }
    }
}
