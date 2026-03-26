using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh.NativeTess
{
    internal struct NativeTessState : IDisposable
    {
        public TessMesh mesh;
        public TessPQ pq;
        public TessDict dict;
        public UnsafeList<ActiveRegion> regions;
        public UnsafeList<int> regionFree;
        public FreeList regionFreeList;

        public int eventVertex;
        public WindingRule windingRule;
        public float3 normal;
        public float3 sUnit;
        public float3 tUnit;
        public float bminX, bminY, bmaxX, bmaxY;
        [MarshalAs(UnmanagedType.U1)]
        public bool removeEmptyPolygons;

        private const int Undef = -1;
        public const float SentinelCoord = 4e30f;

        public static NativeTessState Create(int vertCount, Allocator allocator, bool trackProvenance = true)
        {
            int edgeCount = vertCount * 4;
            int faceCount = vertCount + 4;
            int regionCount = vertCount + 16;

            return new NativeTessState
            {
                mesh = TessMesh.Create(vertCount + 8, edgeCount + 16, faceCount + 8, allocator, trackProvenance),
                pq = TessPQ.Create(vertCount + 8, allocator),
                dict = TessDict.Create(regionCount, allocator),
                regions = new UnsafeList<ActiveRegion>(regionCount, allocator),
                regionFree = new UnsafeList<int>(regionCount, allocator),
                regionFreeList = new FreeList(0),
                eventVertex = Undef,
                windingRule = WindingRule.EvenOdd,
            };
        }

        public void Dispose()
        {
            mesh.Dispose();
            pq.Dispose();
            dict.Dispose();
            if (regions.IsCreated) regions.Dispose();
            if (regionFree.IsCreated) regionFree.Dispose();
        }

        public int AllocRegion()
        {
            int idx;
            if (regionFreeList.HasFree)
            {
                idx = regionFreeList.Pop(ref regionFree);
            }
            else
            {
                idx = regions.Length;
                regions.Add(default);
                regionFree.Add(0);
            }
            return idx;
        }

        public void FreeRegion(int idx)
        {
            regionFreeList.Push(ref regionFree, idx);
        }

        public int RegionBelow(int reg)
        {
            int nodeUp = regions[reg].nodeUp;
            int prevNode = dict.NodePrev(nodeUp);
            return dict.NodeKey(prevNode);
        }

        public int RegionAbove(int reg)
        {
            int nodeUp = regions[reg].nodeUp;
            int nextNode = dict.NodeNext(nodeUp);
            return dict.NodeKey(nextNode);
        }

        public bool EdgeLeq(int reg1Idx, int reg2Idx)
        {
            ref var reg1 = ref regions.ElementAt(reg1Idx);
            ref var reg2 = ref regions.ElementAt(reg2Idx);
            int e1 = reg1.eUp;
            int e2 = reg2.eUp;

            if (mesh.Dst(e1) == eventVertex)
            {
                if (mesh.Dst(e2) == eventVertex)
                {
                    if (Geom.VertLeq(mesh.vertices[mesh.Org(e1)], mesh.vertices[mesh.Org(e2)]))
                        return Geom.EdgeSign(mesh.vertices[mesh.Dst(e2)], mesh.vertices[mesh.Org(e1)], mesh.vertices[mesh.Org(e2)]) <= 0.0f;
                    return Geom.EdgeSign(mesh.vertices[mesh.Dst(e1)], mesh.vertices[mesh.Org(e2)], mesh.vertices[mesh.Org(e1)]) >= 0.0f;
                }
                return Geom.EdgeSign(mesh.vertices[mesh.Dst(e2)], mesh.vertices[eventVertex], mesh.vertices[mesh.Org(e2)]) <= 0.0f;
            }
            if (mesh.Dst(e2) == eventVertex)
            {
                return Geom.EdgeSign(mesh.vertices[mesh.Dst(e1)], mesh.vertices[eventVertex], mesh.vertices[mesh.Org(e1)]) >= 0.0f;
            }

            float t1 = Geom.EdgeEval(mesh.vertices[mesh.Dst(e1)], mesh.vertices[eventVertex], mesh.vertices[mesh.Org(e1)]);
            float t2 = Geom.EdgeEval(mesh.vertices[mesh.Dst(e2)], mesh.vertices[eventVertex], mesh.vertices[mesh.Org(e2)]);
            return (t1 >= t2);
        }
    }
}
