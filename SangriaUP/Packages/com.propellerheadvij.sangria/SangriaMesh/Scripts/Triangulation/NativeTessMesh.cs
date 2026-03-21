using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh.NativeTess
{
    internal struct TessMesh : IDisposable
    {
        public UnsafeList<SweepVertex> vertices;
        public UnsafeList<SweepHalfEdge> edges;
        public UnsafeList<SweepFace> faces;

        public UnsafeList<int> vertexFree;
        public UnsafeList<int> edgeFree;
        public UnsafeList<int> faceFree;

        public FreeList vertexFreeList;
        public FreeList edgeFreeList;
        public FreeList faceFreeList;

        public int vHead;
        public int fHead;
        public int eHead;
        public int eHeadSym;

        private const int Undef = -1;

        public static TessMesh Create(int vertexCapacity, int edgeCapacity, int faceCapacity, Allocator allocator)
        {
            var mesh = new TessMesh
            {
                vertices = new UnsafeList<SweepVertex>(vertexCapacity, allocator),
                edges = new UnsafeList<SweepHalfEdge>(edgeCapacity, allocator),
                faces = new UnsafeList<SweepFace>(faceCapacity, allocator),
                vertexFree = new UnsafeList<int>(vertexCapacity, allocator),
                edgeFree = new UnsafeList<int>(edgeCapacity, allocator),
                faceFree = new UnsafeList<int>(faceCapacity, allocator),
                vertexFreeList = new FreeList(0),
                edgeFreeList = new FreeList(0),
                faceFreeList = new FreeList(0),
            };

            mesh.Init();
            return mesh;
        }

        private void Init()
        {
            int v = AllocVertex();
            int f = AllocFace();

            int e = AllocEdgePair(out int eSym);

            vHead = v;
            fHead = f;
            eHead = e;
            eHeadSym = eSym;

            vertices[v] = new SweepVertex { next = v, prev = v, anEdge = Undef };
            faces[f] = new SweepFace { next = f, prev = f, anEdge = Undef, trail = Undef, inside = false, marked = false };

            ref var eRef = ref edges.ElementAt(e);
            eRef.next = e;
            eRef.sym = eSym;
            eRef.onext = Undef;
            eRef.lnext = Undef;
            eRef.org = Undef;
            eRef.lface = Undef;
            eRef.winding = 0;
            eRef.activeRegion = Undef;

            ref var eSymRef = ref edges.ElementAt(eSym);
            eSymRef.next = eSym;
            eSymRef.sym = e;
            eSymRef.onext = Undef;
            eSymRef.lnext = Undef;
            eSymRef.org = Undef;
            eSymRef.lface = Undef;
            eSymRef.winding = 0;
            eSymRef.activeRegion = Undef;
        }

        public void Dispose()
        {
            if (vertices.IsCreated) vertices.Dispose();
            if (edges.IsCreated) edges.Dispose();
            if (faces.IsCreated) faces.Dispose();
            if (vertexFree.IsCreated) vertexFree.Dispose();
            if (edgeFree.IsCreated) edgeFree.Dispose();
            if (faceFree.IsCreated) faceFree.Dispose();
        }

        public void Reset()
        {
            vertices.Clear();
            edges.Clear();
            faces.Clear();
            vertexFree.Clear();
            edgeFree.Clear();
            faceFree.Clear();
            vertexFreeList = new FreeList(0);
            edgeFreeList = new FreeList(0);
            faceFreeList = new FreeList(0);
            Init();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Sym(int e) => edges[e].sym;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Onext(int e) => edges[e].onext;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Lnext(int e) => edges[e].lnext;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Org(int e) => edges[e].org;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Dst(int e) => edges[Sym(e)].org;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Lface(int e) => edges[e].lface;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Rface(int e) => edges[Sym(e)].lface;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Oprev(int e) => edges[Sym(e)].lnext;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Lprev(int e) => edges[Onext(e)].sym;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Dprev(int e) => edges[Lnext(e)].sym;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Rprev(int e) => edges[Sym(e)].onext;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Dnext(int e) => edges[Rprev(e)].sym;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Rnext(int e) => edges[Oprev(e)].sym;

        private int AllocVertex()
        {
            int idx;
            if (vertexFreeList.HasFree)
            {
                idx = vertexFreeList.Pop(ref vertexFree);
            }
            else
            {
                idx = vertices.Length;
                vertices.Add(default);
                vertexFree.Add(0);
            }
            return idx;
        }

        private int AllocFace()
        {
            int idx;
            if (faceFreeList.HasFree)
            {
                idx = faceFreeList.Pop(ref faceFree);
            }
            else
            {
                idx = faces.Length;
                faces.Add(default);
                faceFree.Add(0);
            }
            return idx;
        }

        private int AllocEdge()
        {
            int idx;
            if (edgeFreeList.HasFree)
            {
                idx = edgeFreeList.Pop(ref edgeFree);
            }
            else
            {
                idx = edges.Length;
                edges.Add(default);
                edgeFree.Add(0);
            }
            return idx;
        }

        private int AllocEdgePair(out int eSym)
        {
            int e = AllocEdge();
            eSym = AllocEdge();

            if (eSym < e)
            {
                int tmp = e;
                e = eSym;
                eSym = tmp;
            }

            return e;
        }

        private void FreeVertex(int idx)
        {
            vertexFreeList.Push(ref vertexFree, idx);
        }

        private void FreeFace(int idx)
        {
            faceFreeList.Push(ref faceFree, idx);
        }

        private void FreeEdge(int idx)
        {
            edgeFreeList.Push(ref edgeFree, idx);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureFirstEdge(ref int e)
        {
            int eSym = edges[e].sym;
            if (eSym < e)
                e = eSym;
        }

        public void Splice(int a, int b)
        {
            int aOnext = edges[a].onext;
            int bOnext = edges[b].onext;

            edges.ElementAt(edges[aOnext].sym).lnext = b;
            edges.ElementAt(edges[bOnext].sym).lnext = a;
            edges.ElementAt(a).onext = bOnext;
            edges.ElementAt(b).onext = aOnext;
        }

        public void MakeVertex(int eOrig, int vNext)
        {
            int vNew = AllocVertex();

            int vPrev = vertices[vNext].prev;
            vertices.ElementAt(vNew) = new SweepVertex
            {
                prev = vPrev,
                next = vNext,
                anEdge = eOrig,
                coords = float3.zero,
                s = 0.0f,
                t = 0.0f,
                pqHandle = default,
                n = 0
            };
            vertices.ElementAt(vPrev).next = vNew;
            vertices.ElementAt(vNext).prev = vNew;

            int e = eOrig;
            do
            {
                edges.ElementAt(e).org = vNew;
                e = edges[e].onext;
            } while (e != eOrig);
        }

        public void MakeFace(int eOrig, int fNext)
        {
            int fNew = AllocFace();

            int fPrev = faces[fNext].prev;
            faces.ElementAt(fNew) = new SweepFace
            {
                prev = fPrev,
                next = fNext,
                anEdge = eOrig,
                trail = Undef,
                n = 0,
                marked = false,
                inside = faces[fNext].inside
            };
            faces.ElementAt(fPrev).next = fNew;
            faces.ElementAt(fNext).prev = fNew;

            int e = eOrig;
            do
            {
                edges.ElementAt(e).lface = fNew;
                e = edges[e].lnext;
            } while (e != eOrig);
        }

        private int MakeEdgeInternal(int eNext)
        {
            int e = AllocEdgePair(out int eSym);

            EnsureFirstEdge(ref eNext);
            int eNextSym = edges[eNext].sym;

            int ePrev = edges[eNextSym].next;
            edges.ElementAt(eSym).next = ePrev;
            edges.ElementAt(edges[ePrev].sym).next = e;
            edges.ElementAt(e).next = eNext;
            edges.ElementAt(eNextSym).next = eSym;

            edges.ElementAt(e).sym = eSym;
            edges.ElementAt(e).onext = e;
            edges.ElementAt(e).lnext = eSym;
            edges.ElementAt(e).org = Undef;
            edges.ElementAt(e).lface = Undef;
            edges.ElementAt(e).winding = 0;
            edges.ElementAt(e).activeRegion = Undef;

            edges.ElementAt(eSym).sym = e;
            edges.ElementAt(eSym).onext = eSym;
            edges.ElementAt(eSym).lnext = e;
            edges.ElementAt(eSym).org = Undef;
            edges.ElementAt(eSym).lface = Undef;
            edges.ElementAt(eSym).winding = 0;
            edges.ElementAt(eSym).activeRegion = Undef;

            return e;
        }

        public int MeshMakeEdge()
        {
            int e = MakeEdgeInternal(eHead);
            MakeVertex(e, vHead);
            MakeVertex(edges[e].sym, vHead);
            MakeFace(e, fHead);
            return e;
        }

        public void MeshSplice(int eOrg, int eDst)
        {
            if (eOrg == eDst) return;

            bool joiningVertices = false;
            if (edges[eDst].org != edges[eOrg].org)
            {
                joiningVertices = true;
                KillVertex(edges[eDst].org, edges[eOrg].org);
            }
            bool joiningLoops = false;
            if (edges[eDst].lface != edges[eOrg].lface)
            {
                joiningLoops = true;
                KillFace(edges[eDst].lface, edges[eOrg].lface);
            }

            Splice(eDst, eOrg);

            if (!joiningVertices)
            {
                MakeVertex(eDst, edges[eOrg].org);
                vertices.ElementAt(edges[eOrg].org).anEdge = eOrg;
            }
            if (!joiningLoops)
            {
                MakeFace(eDst, edges[eOrg].lface);
                faces.ElementAt(edges[eOrg].lface).anEdge = eOrg;
            }
        }

        public void MeshDelete(int eDel)
        {
            int eDelSym = edges[eDel].sym;

            bool joiningLoops = false;
            if (edges[eDel].lface != Rface(eDel))
            {
                joiningLoops = true;
                KillFace(edges[eDel].lface, Rface(eDel));
            }

            if (edges[eDel].onext == eDel)
            {
                KillVertex(edges[eDel].org, Undef);
            }
            else
            {
                faces.ElementAt(Rface(eDel)).anEdge = Oprev(eDel);
                vertices.ElementAt(edges[eDel].org).anEdge = edges[eDel].onext;
                Splice(eDel, Oprev(eDel));
                if (!joiningLoops)
                    MakeFace(eDel, edges[eDel].lface);
            }

            if (edges[eDelSym].onext == eDelSym)
            {
                KillVertex(edges[eDelSym].org, Undef);
                KillFace(edges[eDelSym].lface, Undef);
            }
            else
            {
                faces.ElementAt(edges[eDel].lface).anEdge = Oprev(eDelSym);
                vertices.ElementAt(edges[eDelSym].org).anEdge = edges[eDelSym].onext;
                Splice(eDelSym, Oprev(eDelSym));
            }

            KillEdge(eDel);
        }

        public int AddEdgeVertex(int eOrg)
        {
            int eNew = MakeEdgeInternal(eOrg);
            int eNewSym = edges[eNew].sym;

            Splice(eNew, Lnext(eOrg));

            edges.ElementAt(eNew).org = Dst(eOrg);
            MakeVertex(eNewSym, edges[eNew].org);
            edges.ElementAt(eNew).lface = edges[eOrg].lface;
            edges.ElementAt(eNewSym).lface = edges[eOrg].lface;

            return eNew;
        }

        public int SplitEdge(int eOrg)
        {
            int eTmp = AddEdgeVertex(eOrg);
            int eNew = edges[eTmp].sym;

            Splice(edges[eOrg].sym, Oprev(edges[eOrg].sym));
            Splice(edges[eOrg].sym, eNew);

            edges.ElementAt(edges[eOrg].sym).org = edges[eNew].org;
            vertices.ElementAt(Dst(eNew)).anEdge = edges[eNew].sym;
            edges.ElementAt(edges[eNew].sym).lface = Rface(eOrg);
            edges.ElementAt(eNew).winding = edges[eOrg].winding;
            edges.ElementAt(edges[eNew].sym).winding = edges[edges[eOrg].sym].winding;

            return eNew;
        }

        public int Connect(int eOrg, int eDst)
        {
            int eNew = MakeEdgeInternal(eOrg);
            int eNewSym = edges[eNew].sym;

            bool joiningLoops = false;
            if (edges[eDst].lface != edges[eOrg].lface)
            {
                joiningLoops = true;
                KillFace(edges[eDst].lface, edges[eOrg].lface);
            }

            Splice(eNew, Lnext(eOrg));
            Splice(eNewSym, eDst);

            edges.ElementAt(eNew).org = Dst(eOrg);
            edges.ElementAt(eNewSym).org = edges[eDst].org;
            edges.ElementAt(eNew).lface = edges[eOrg].lface;
            edges.ElementAt(eNewSym).lface = edges[eOrg].lface;

            faces.ElementAt(edges[eOrg].lface).anEdge = eNewSym;

            if (!joiningLoops)
                MakeFace(eNew, edges[eOrg].lface);

            return eNew;
        }

        public void ZapFace(int fZap)
        {
            int eStart = faces[fZap].anEdge;
            int eNext = edges[eStart].lnext;
            int e;
            do
            {
                e = eNext;
                eNext = edges[e].lnext;

                edges.ElementAt(e).lface = Undef;
                if (Rface(e) == Undef)
                {
                    if (edges[e].onext == e)
                    {
                        KillVertex(edges[e].org, Undef);
                    }
                    else
                    {
                        vertices.ElementAt(edges[e].org).anEdge = edges[e].onext;
                        Splice(e, Oprev(e));
                    }
                    int eSym = edges[e].sym;
                    if (edges[eSym].onext == eSym)
                    {
                        KillVertex(edges[eSym].org, Undef);
                    }
                    else
                    {
                        vertices.ElementAt(edges[eSym].org).anEdge = edges[eSym].onext;
                        Splice(eSym, Oprev(eSym));
                    }
                    KillEdge(e);
                }
            } while (e != eStart);

            int fPrev = faces[fZap].prev;
            int fNext = faces[fZap].next;
            faces.ElementAt(fNext).prev = fPrev;
            faces.ElementAt(fPrev).next = fNext;
            FreeFace(fZap);
        }

        public void KillVertex(int vDel, int newOrg)
        {
            int eStart = vertices[vDel].anEdge;
            if (eStart != Undef)
            {
                int e = eStart;
                do
                {
                    edges.ElementAt(e).org = newOrg;
                    e = edges[e].onext;
                } while (e != eStart);
            }

            int vPrev = vertices[vDel].prev;
            int vNext = vertices[vDel].next;
            if (vNext != Undef) vertices.ElementAt(vNext).prev = vPrev;
            if (vPrev != Undef) vertices.ElementAt(vPrev).next = vNext;
            FreeVertex(vDel);
        }

        public void KillFace(int fDel, int newLFace)
        {
            int eStart = faces[fDel].anEdge;
            if (eStart != Undef)
            {
                int e = eStart;
                do
                {
                    edges.ElementAt(e).lface = newLFace;
                    e = edges[e].lnext;
                } while (e != eStart);
            }

            int fPrev = faces[fDel].prev;
            int fNext = faces[fDel].next;
            if (fNext != Undef) faces.ElementAt(fNext).prev = fPrev;
            if (fPrev != Undef) faces.ElementAt(fPrev).next = fNext;
            FreeFace(fDel);
        }

        public void KillEdge(int eDel)
        {
            EnsureFirstEdge(ref eDel);
            int eDelSym = edges[eDel].sym;

            int eNext = edges[eDel].next;
            int ePrev = edges[edges[eDel].sym].next;
            edges.ElementAt(edges[eNext].sym).next = ePrev;
            edges.ElementAt(edges[ePrev].sym).next = eNext;

            FreeEdge(edges[eDel].sym);
            FreeEdge(eDel);
        }

        public void AddWinding(int eDst, int eSrc)
        {
            edges.ElementAt(eDst).winding += edges[eSrc].winding;
            edges.ElementAt(edges[eDst].sym).winding += edges[edges[eSrc].sym].winding;
        }
    }
}
