using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh.NativeTess
{
    [BurstCompile]
    internal static class Sweep
    {
        private const int Undef = -1;

        private static void SetEdgeSplitProvenance(ref TessMesh mesh, int vertexId, in ProvenanceRecord source)
        {
            mesh.vertexProvenance[vertexId] = source;
            mesh.vertexProvenance.ElementAt(vertexId).Kind = ProvenanceKind.EdgeSplit;
        }

        public static void DeleteRegion(ref NativeTessState s, int reg)
        {
            if (s.regions[reg].fixUpperEdge)
            {
                // zero winding check omitted in release
            }
            int eUp = s.regions[reg].eUp;
            s.mesh.edges.ElementAt(eUp).activeRegion = Undef;
            s.dict.Remove(s.regions[reg].nodeUp);
            s.FreeRegion(reg);
        }

        public static void FixUpperEdge(ref NativeTessState s, int reg, int newEdge)
        {
            s.mesh.MeshDelete(s.regions[reg].eUp);
            s.regions.ElementAt(reg).fixUpperEdge = false;
            s.regions.ElementAt(reg).eUp = newEdge;
            s.mesh.edges.ElementAt(newEdge).activeRegion = reg;
        }

        public static int AddRegionBelow(ref NativeTessState s, int regAbove, int eNewUp)
        {
            int regNew = s.AllocRegion();
            s.regions.ElementAt(regNew).eUp = eNewUp;
            s.regions.ElementAt(regNew).nodeUp = s.dict.InsertBefore(s.regions[regAbove].nodeUp, regNew, s);
            s.regions.ElementAt(regNew).fixUpperEdge = false;
            s.regions.ElementAt(regNew).sentinel = false;
            s.regions.ElementAt(regNew).dirty = false;
            s.regions.ElementAt(regNew).inside = false;
            s.regions.ElementAt(regNew).windingNumber = 0;
            s.mesh.edges.ElementAt(eNewUp).activeRegion = regNew;
            return regNew;
        }

        public static void ComputeWinding(ref NativeTessState s, int reg)
        {
            int above = s.RegionAbove(reg);
            s.regions.ElementAt(reg).windingNumber = s.regions[above].windingNumber + s.mesh.edges[s.regions[reg].eUp].winding;
            s.regions.ElementAt(reg).inside = Geom.IsWindingInside(s.windingRule, s.regions[reg].windingNumber);
        }

        public static void FinishRegion(ref NativeTessState s, int reg)
        {
            int e = s.regions[reg].eUp;
            int f = s.mesh.Lface(e);
            s.mesh.faces.ElementAt(f).inside = s.regions[reg].inside;
            s.mesh.faces.ElementAt(f).anEdge = e;
            DeleteRegion(ref s, reg);
        }

        public static int FinishLeftRegions(ref NativeTessState s, int regFirst, int regLast)
        {
            int regPrev = regFirst;
            int ePrev = s.regions[regFirst].eUp;

            while (regPrev != regLast)
            {
                s.regions.ElementAt(regPrev).fixUpperEdge = false;
                int reg = s.RegionBelow(regPrev);
                int e = s.regions[reg].eUp;
                if (s.mesh.Org(e) != s.mesh.Org(ePrev))
                {
                    if (!s.regions[reg].fixUpperEdge)
                    {
                        FinishRegion(ref s, regPrev);
                        break;
                    }
                    e = s.mesh.Connect(s.mesh.Lprev(ePrev), s.mesh.Sym(e));
                    FixUpperEdge(ref s, reg, e);
                }

                if (s.mesh.Onext(ePrev) != e)
                {
                    s.mesh.MeshSplice(s.mesh.Oprev(e), e);
                    s.mesh.MeshSplice(ePrev, e);
                }
                FinishRegion(ref s, regPrev);
                ePrev = s.regions[reg].eUp;
                regPrev = reg;
            }
            return ePrev;
        }

        public static void AddRightEdges(ref NativeTessState s, int regUp, int eFirst, int eLast, int eTopLeft, bool cleanUp)
        {
            bool firstTime = true;
            int e = eFirst;
            do
            {
                AddRegionBelow(ref s, regUp, s.mesh.Sym(e));
                e = s.mesh.Onext(e);
            } while (e != eLast);

            if (eTopLeft == Undef)
                eTopLeft = s.mesh.Rprev(s.regions[s.RegionBelow(regUp)].eUp);

            int regPrev = regUp;
            int ePrev = eTopLeft;
            while (true)
            {
                int reg = s.RegionBelow(regPrev);
                e = s.mesh.Sym(s.regions[reg].eUp);
                if (s.mesh.Org(e) != s.mesh.Org(ePrev)) break;

                if (s.mesh.Onext(e) != ePrev)
                {
                    s.mesh.MeshSplice(s.mesh.Oprev(e), e);
                    s.mesh.MeshSplice(s.mesh.Oprev(ePrev), e);
                }
                s.regions.ElementAt(reg).windingNumber = s.regions[regPrev].windingNumber - s.mesh.edges[e].winding;
                s.regions.ElementAt(reg).inside = Geom.IsWindingInside(s.windingRule, s.regions[reg].windingNumber);

                s.regions.ElementAt(regPrev).dirty = true;
                if (!firstTime && CheckForRightSplice(ref s, regPrev))
                {
                    s.mesh.AddWinding(e, ePrev);
                    DeleteRegion(ref s, regPrev);
                    s.mesh.MeshDelete(ePrev);
                }
                firstTime = false;
                regPrev = reg;
                ePrev = e;
            }
            s.regions.ElementAt(regPrev).dirty = true;

            if (cleanUp)
                WalkDirtyRegions(ref s, regPrev);
        }

        public static void SpliceMergeVertices(ref NativeTessState s, int e1, int e2)
        {
            if (s.mesh.trackProvenance)
            {
                int v1 = s.mesh.Org(e1);
                int v2 = s.mesh.Org(e2);
                var prov1 = s.mesh.vertexProvenance[v1];
                var prov2 = s.mesh.vertexProvenance[v2];

                ProvenanceRecord combined;
                // Fast path: both Identity with same source
                if (prov1.Count == 1 && prov2.Count == 1 && prov1.Src0 == prov2.Src0)
                {
                    combined = prov1;
                    combined.Kind = ProvenanceKind.Merge;
                }
                else
                {
                    combined = ProvenanceRecord.Combine(in prov1, in prov2, ProvenanceKind.Merge);
                }

                s.mesh.MeshSplice(e1, e2);
                int survivor = s.mesh.Org(e1);
                s.mesh.vertexProvenance[survivor] = combined;
            }
            else
            {
                s.mesh.MeshSplice(e1, e2);
            }
        }

        public static void GetIntersectData(ref NativeTessState s, int isect, int orgUp, int dstUp, int orgLo, int dstLo)
        {
            s.mesh.vertices.ElementAt(isect).coords = float3.zero;
            float t1 = Geom.VertL1dist(s.mesh.vertices[orgUp], s.mesh.vertices[isect]);
            float t2 = Geom.VertL1dist(s.mesh.vertices[dstUp], s.mesh.vertices[isect]);
            float w0 = (t2 / (t1 + t2)) / 2.0f;
            float w1 = (t1 / (t1 + t2)) / 2.0f;
            s.mesh.vertices.ElementAt(isect).coords += w0 * s.mesh.vertices[orgUp].coords + w1 * s.mesh.vertices[dstUp].coords;

            t1 = Geom.VertL1dist(s.mesh.vertices[orgLo], s.mesh.vertices[isect]);
            t2 = Geom.VertL1dist(s.mesh.vertices[dstLo], s.mesh.vertices[isect]);
            float w2 = (t2 / (t1 + t2)) / 2.0f;
            float w3 = (t1 / (t1 + t2)) / 2.0f;
            s.mesh.vertices.ElementAt(isect).coords += w2 * s.mesh.vertices[orgLo].coords + w3 * s.mesh.vertices[dstLo].coords;

            if (s.mesh.trackProvenance)
            {
                var provOrgUp = s.mesh.vertexProvenance[orgUp];
                var provDstUp = s.mesh.vertexProvenance[dstUp];
                var provOrgLo = s.mesh.vertexProvenance[orgLo];
                var provDstLo = s.mesh.vertexProvenance[dstLo];
                s.mesh.vertexProvenance[isect] = ProvenanceRecord.CombineWeighted(
                    in provOrgUp, w0,
                    in provDstUp, w1,
                    in provOrgLo, w2,
                    in provDstLo, w3,
                    ProvenanceKind.Intersection);
            }
        }

        public static bool CheckForRightSplice(ref NativeTessState s, int regUp)
        {
            int regLo = s.RegionBelow(regUp);
            int eUp = s.regions[regUp].eUp;
            int eLo = s.regions[regLo].eUp;

            if (Geom.VertLeq(s.mesh.vertices[s.mesh.Org(eUp)], s.mesh.vertices[s.mesh.Org(eLo)]))
            {
                if (Geom.EdgeSign(s.mesh.vertices[s.mesh.Dst(eLo)], s.mesh.vertices[s.mesh.Org(eUp)], s.mesh.vertices[s.mesh.Org(eLo)]) > 0.0f)
                    return false;

                if (!Geom.VertEq(s.mesh.vertices[s.mesh.Org(eUp)], s.mesh.vertices[s.mesh.Org(eLo)]))
                {
                    s.mesh.SplitEdge(s.mesh.Sym(eLo));
                    if (s.mesh.trackProvenance)
                    {
                        var provUp = s.mesh.vertexProvenance[s.mesh.Org(eUp)];
                        SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(eLo), in provUp);
                    }
                    s.mesh.MeshSplice(eUp, s.mesh.Oprev(eLo));
                    s.regions.ElementAt(regUp).dirty = true;
                    s.regions.ElementAt(regLo).dirty = true;
                }
                else if (s.mesh.Org(eUp) != s.mesh.Org(eLo))
                {
                    s.pq.Remove(s.mesh.vertices[s.mesh.Org(eUp)].pqHandle, ref s.mesh.vertices);
                    SpliceMergeVertices(ref s, s.mesh.Oprev(eLo), eUp);
                }
            }
            else
            {
                if (Geom.EdgeSign(s.mesh.vertices[s.mesh.Dst(eUp)], s.mesh.vertices[s.mesh.Org(eLo)], s.mesh.vertices[s.mesh.Org(eUp)]) < 0.0f)
                    return false;

                s.regions.ElementAt(s.RegionAbove(regUp)).dirty = true;
                s.regions.ElementAt(regUp).dirty = true;
                s.mesh.SplitEdge(s.mesh.Sym(eUp));
                if (s.mesh.trackProvenance)
                {
                    var provLo = s.mesh.vertexProvenance[s.mesh.Org(eLo)];
                    SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(eUp), in provLo);
                }
                s.mesh.MeshSplice(s.mesh.Oprev(eLo), eUp);
            }
            return true;
        }

        public static bool CheckForLeftSplice(ref NativeTessState s, int regUp)
        {
            int regLo = s.RegionBelow(regUp);
            int eUp = s.regions[regUp].eUp;
            int eLo = s.regions[regLo].eUp;

            if (Geom.VertLeq(s.mesh.vertices[s.mesh.Dst(eUp)], s.mesh.vertices[s.mesh.Dst(eLo)]))
            {
                if (Geom.EdgeSign(s.mesh.vertices[s.mesh.Dst(eUp)], s.mesh.vertices[s.mesh.Dst(eLo)], s.mesh.vertices[s.mesh.Org(eUp)]) < 0.0f)
                    return false;

                s.regions.ElementAt(s.RegionAbove(regUp)).dirty = true;
                s.regions.ElementAt(regUp).dirty = true;
                int e = s.mesh.SplitEdge(eUp);
                if (s.mesh.trackProvenance)
                {
                    var provDstLo = s.mesh.vertexProvenance[s.mesh.Dst(eLo)];
                    SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(e), in provDstLo);
                }
                s.mesh.MeshSplice(s.mesh.Sym(eLo), e);
                s.mesh.faces.ElementAt(s.mesh.Lface(e)).inside = s.regions[regUp].inside;
            }
            else
            {
                if (Geom.EdgeSign(s.mesh.vertices[s.mesh.Dst(eLo)], s.mesh.vertices[s.mesh.Dst(eUp)], s.mesh.vertices[s.mesh.Org(eLo)]) > 0.0f)
                    return false;

                s.regions.ElementAt(regUp).dirty = true;
                s.regions.ElementAt(regLo).dirty = true;
                int e = s.mesh.SplitEdge(eLo);
                if (s.mesh.trackProvenance)
                {
                    var provDstUp = s.mesh.vertexProvenance[s.mesh.Dst(eUp)];
                    SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(e), in provDstUp);
                }
                s.mesh.MeshSplice(s.mesh.Lnext(eUp), s.mesh.Sym(eLo));
                s.mesh.faces.ElementAt(s.mesh.Rface(e)).inside = s.regions[regUp].inside;
            }
            return true;
        }

        public static bool CheckForIntersect(ref NativeTessState s, int regUp)
        {
            int regLo = s.RegionBelow(regUp);
            int eUp = s.regions[regUp].eUp;
            int eLo = s.regions[regLo].eUp;
            int orgUp = s.mesh.Org(eUp);
            int orgLo = s.mesh.Org(eLo);
            int dstUp = s.mesh.Dst(eUp);
            int dstLo = s.mesh.Dst(eLo);

            if (orgUp == orgLo) return false;

            float tMinUp = MathExtensions.FastMin(s.mesh.vertices[orgUp].t, s.mesh.vertices[dstUp].t);
            float tMaxLo = MathExtensions.FastMax(s.mesh.vertices[orgLo].t, s.mesh.vertices[dstLo].t);
            if (tMinUp > tMaxLo) return false;

            if (Geom.VertLeq(s.mesh.vertices[orgUp], s.mesh.vertices[orgLo]))
            {
                if (Geom.EdgeSign(s.mesh.vertices[dstLo], s.mesh.vertices[orgUp], s.mesh.vertices[orgLo]) > 0.0f)
                    return false;
            }
            else
            {
                if (Geom.EdgeSign(s.mesh.vertices[dstUp], s.mesh.vertices[orgLo], s.mesh.vertices[orgUp]) < 0.0f)
                    return false;
            }

            SweepVertex isectVert = default;
            Geom.EdgeIntersect(s.mesh.vertices[dstUp], s.mesh.vertices[orgUp], s.mesh.vertices[dstLo], s.mesh.vertices[orgLo], ref isectVert);

            if (Geom.VertLeq(isectVert, s.mesh.vertices[s.eventVertex]))
            {
                isectVert.s = s.mesh.vertices[s.eventVertex].s;
                isectVert.t = s.mesh.vertices[s.eventVertex].t;
            }

            int orgMinV = Geom.VertLeq(s.mesh.vertices[orgUp], s.mesh.vertices[orgLo]) ? orgUp : orgLo;
            if (Geom.VertLeq(s.mesh.vertices[orgMinV], isectVert))
            {
                isectVert.s = s.mesh.vertices[orgMinV].s;
                isectVert.t = s.mesh.vertices[orgMinV].t;
            }

            if (Geom.VertEq(isectVert, s.mesh.vertices[orgUp]) || Geom.VertEq(isectVert, s.mesh.vertices[orgLo]))
            {
                CheckForRightSplice(ref s, regUp);
                return false;
            }

            if ((!Geom.VertEq(s.mesh.vertices[dstUp], s.mesh.vertices[s.eventVertex])
                && Geom.EdgeSign(s.mesh.vertices[dstUp], s.mesh.vertices[s.eventVertex], isectVert) >= 0.0f)
                || (!Geom.VertEq(s.mesh.vertices[dstLo], s.mesh.vertices[s.eventVertex])
                && Geom.EdgeSign(s.mesh.vertices[dstLo], s.mesh.vertices[s.eventVertex], isectVert) <= 0.0f))
            {
                if (dstLo == s.eventVertex)
                {
                    s.mesh.SplitEdge(s.mesh.Sym(eUp));
                    if (s.mesh.trackProvenance)
                    {
                        var provEvent1 = s.mesh.vertexProvenance[s.eventVertex];
                        SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(eUp), in provEvent1);
                    }
                    s.mesh.MeshSplice(s.mesh.Sym(eLo), eUp);
                    regUp = TopLeftRegion(ref s, regUp);
                    eUp = s.regions[s.RegionBelow(regUp)].eUp;
                    FinishLeftRegions(ref s, s.RegionBelow(regUp), regLo);
                    AddRightEdges(ref s, regUp, s.mesh.Oprev(eUp), eUp, eUp, true);
                    return true;
                }
                if (dstUp == s.eventVertex)
                {
                    s.mesh.SplitEdge(s.mesh.Sym(eLo));
                    if (s.mesh.trackProvenance)
                    {
                        var provEvent2 = s.mesh.vertexProvenance[s.eventVertex];
                        SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(eLo), in provEvent2);
                    }
                    s.mesh.MeshSplice(s.mesh.Lnext(eUp), s.mesh.Oprev(eLo));
                    regLo = regUp;
                    regUp = TopRightRegion(ref s, regUp);
                    int e = s.mesh.Rprev(s.regions[s.RegionBelow(regUp)].eUp);
                    s.regions.ElementAt(regLo).eUp = s.mesh.Oprev(eLo);
                    eLo = FinishLeftRegions(ref s, regLo, Undef);
                    AddRightEdges(ref s, regUp, s.mesh.Onext(eLo), s.mesh.Rprev(eUp), e, true);
                    return true;
                }
                if (Geom.EdgeSign(s.mesh.vertices[dstUp], s.mesh.vertices[s.eventVertex], isectVert) >= 0.0f)
                {
                    s.regions.ElementAt(s.RegionAbove(regUp)).dirty = true;
                    s.regions.ElementAt(regUp).dirty = true;
                    s.mesh.SplitEdge(s.mesh.Sym(eUp));
                    if (s.mesh.trackProvenance)
                    {
                        var provEvent3 = s.mesh.vertexProvenance[s.eventVertex];
                        SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(eUp), in provEvent3);
                    }
                    s.mesh.vertices.ElementAt(s.mesh.Org(eUp)).s = s.mesh.vertices[s.eventVertex].s;
                    s.mesh.vertices.ElementAt(s.mesh.Org(eUp)).t = s.mesh.vertices[s.eventVertex].t;
                }
                if (Geom.EdgeSign(s.mesh.vertices[dstLo], s.mesh.vertices[s.eventVertex], isectVert) <= 0.0f)
                {
                    s.regions.ElementAt(regUp).dirty = true;
                    s.regions.ElementAt(regLo).dirty = true;
                    s.mesh.SplitEdge(s.mesh.Sym(eLo));
                    if (s.mesh.trackProvenance)
                    {
                        var provEvent4 = s.mesh.vertexProvenance[s.eventVertex];
                        SetEdgeSplitProvenance(ref s.mesh, s.mesh.Org(eLo), in provEvent4);
                    }
                    s.mesh.vertices.ElementAt(s.mesh.Org(eLo)).s = s.mesh.vertices[s.eventVertex].s;
                    s.mesh.vertices.ElementAt(s.mesh.Org(eLo)).t = s.mesh.vertices[s.eventVertex].t;
                }
                return false;
            }

            s.mesh.SplitEdge(s.mesh.Sym(eUp));
            s.mesh.SplitEdge(s.mesh.Sym(eLo));
            s.mesh.MeshSplice(s.mesh.Oprev(eLo), eUp);
            s.mesh.vertices.ElementAt(s.mesh.Org(eUp)).s = isectVert.s;
            s.mesh.vertices.ElementAt(s.mesh.Org(eUp)).t = isectVert.t;
            s.mesh.vertices.ElementAt(s.mesh.Org(eUp)).coords = isectVert.coords;
            var pqh = s.pq.Insert(s.mesh.Org(eUp), ref s.mesh.vertices);
            s.mesh.vertices.ElementAt(s.mesh.Org(eUp)).pqHandle = pqh;
            GetIntersectData(ref s, s.mesh.Org(eUp), orgUp, dstUp, orgLo, dstLo);
            s.regions.ElementAt(s.RegionAbove(regUp)).dirty = true;
            s.regions.ElementAt(regUp).dirty = true;
            s.regions.ElementAt(regLo).dirty = true;
            return false;
        }

        public static void WalkDirtyRegions(ref NativeTessState s, int regUp)
        {
            int regLo = s.RegionBelow(regUp);

            while (true)
            {
                while (s.regions[regLo].dirty)
                {
                    regUp = regLo;
                    regLo = s.RegionBelow(regLo);
                }
                if (!s.regions[regUp].dirty)
                {
                    regLo = regUp;
                    regUp = s.RegionAbove(regUp);
                    if (regUp == Undef || !s.regions[regUp].dirty)
                        return;
                }
                s.regions.ElementAt(regUp).dirty = false;
                int eUp = s.regions[regUp].eUp;
                int eLo = s.regions[regLo].eUp;

                if (s.mesh.Dst(eUp) != s.mesh.Dst(eLo))
                {
                    if (CheckForLeftSplice(ref s, regUp))
                    {
                        if (s.regions[regLo].fixUpperEdge)
                        {
                            DeleteRegion(ref s, regLo);
                            s.mesh.MeshDelete(eLo);
                            regLo = s.RegionBelow(regUp);
                            eLo = s.regions[regLo].eUp;
                        }
                        else if (s.regions[regUp].fixUpperEdge)
                        {
                            DeleteRegion(ref s, regUp);
                            s.mesh.MeshDelete(eUp);
                            regUp = s.RegionAbove(regLo);
                            eUp = s.regions[regUp].eUp;
                        }
                    }
                }
                if (s.mesh.Org(eUp) != s.mesh.Org(eLo))
                {
                    if (s.mesh.Dst(eUp) != s.mesh.Dst(eLo)
                        && !s.regions[regUp].fixUpperEdge && !s.regions[regLo].fixUpperEdge
                        && (s.mesh.Dst(eUp) == s.eventVertex || s.mesh.Dst(eLo) == s.eventVertex))
                    {
                        if (CheckForIntersect(ref s, regUp))
                            return;
                    }
                    else
                    {
                        CheckForRightSplice(ref s, regUp);
                    }
                }
                if (s.mesh.Org(eUp) == s.mesh.Org(eLo) && s.mesh.Dst(eUp) == s.mesh.Dst(eLo))
                {
                    s.mesh.AddWinding(eLo, eUp);
                    DeleteRegion(ref s, regUp);
                    s.mesh.MeshDelete(eUp);
                    regUp = s.RegionAbove(regLo);
                }
            }
        }

        public static int TopLeftRegion(ref NativeTessState s, int reg)
        {
            int org = s.mesh.Org(s.regions[reg].eUp);
            do
            {
                reg = s.RegionAbove(reg);
            } while (s.mesh.Org(s.regions[reg].eUp) == org);

            if (s.regions[reg].fixUpperEdge)
            {
                int e = s.mesh.Connect(s.mesh.Sym(s.regions[s.RegionBelow(reg)].eUp), s.mesh.Lnext(s.regions[reg].eUp));
                FixUpperEdge(ref s, reg, e);
                reg = s.RegionAbove(reg);
            }
            return reg;
        }

        public static int TopRightRegion(ref NativeTessState s, int reg)
        {
            int dst = s.mesh.Dst(s.regions[reg].eUp);
            do
            {
                reg = s.RegionAbove(reg);
            } while (s.mesh.Dst(s.regions[reg].eUp) == dst);
            return reg;
        }

        public static void ConnectRightVertex(ref NativeTessState s, int regUp, int eBottomLeft)
        {
            int eTopLeft = s.mesh.Onext(eBottomLeft);
            int regLo = s.RegionBelow(regUp);
            int eUp = s.regions[regUp].eUp;
            int eLo = s.regions[regLo].eUp;
            bool degenerate = false;

            if (s.mesh.Dst(eUp) != s.mesh.Dst(eLo))
                CheckForIntersect(ref s, regUp);

            if (Geom.VertEq(s.mesh.vertices[s.mesh.Org(eUp)], s.mesh.vertices[s.eventVertex]))
            {
                s.mesh.MeshSplice(s.mesh.Oprev(eTopLeft), eUp);
                regUp = TopLeftRegion(ref s, regUp);
                eTopLeft = s.regions[s.RegionBelow(regUp)].eUp;
                FinishLeftRegions(ref s, s.RegionBelow(regUp), regLo);
                degenerate = true;
            }
            if (Geom.VertEq(s.mesh.vertices[s.mesh.Org(eLo)], s.mesh.vertices[s.eventVertex]))
            {
                s.mesh.MeshSplice(eBottomLeft, s.mesh.Oprev(eLo));
                eBottomLeft = FinishLeftRegions(ref s, regLo, Undef);
                degenerate = true;
            }
            if (degenerate)
            {
                AddRightEdges(ref s, regUp, s.mesh.Onext(eBottomLeft), eTopLeft, eTopLeft, true);
                return;
            }

            int eNew;
            if (Geom.VertLeq(s.mesh.vertices[s.mesh.Org(eLo)], s.mesh.vertices[s.mesh.Org(eUp)]))
                eNew = s.mesh.Oprev(eLo);
            else
                eNew = eUp;
            eNew = s.mesh.Connect(s.mesh.Lprev(eBottomLeft), eNew);

            AddRightEdges(ref s, regUp, eNew, s.mesh.Onext(eNew), s.mesh.Onext(eNew), false);
            s.regions.ElementAt(s.mesh.edges[s.mesh.Sym(eNew)].activeRegion).fixUpperEdge = true;
            WalkDirtyRegions(ref s, regUp);
        }

        public static void ConnectLeftDegenerate(ref NativeTessState s, int regUp, int vEvent)
        {
            int e = s.regions[regUp].eUp;
            if (Geom.VertEq(s.mesh.vertices[s.mesh.Org(e)], s.mesh.vertices[vEvent]))
                return; // should have been merged

            if (!Geom.VertEq(s.mesh.vertices[s.mesh.Dst(e)], s.mesh.vertices[vEvent]))
            {
                s.mesh.SplitEdge(s.mesh.Sym(e));
                int newVert = s.mesh.Org(e);
                if (s.mesh.trackProvenance)
                {
                    var provEvent = s.mesh.vertexProvenance[vEvent];
                    provEvent.Kind = ProvenanceKind.Degenerate;
                    s.mesh.vertexProvenance[newVert] = provEvent;
                }
                if (s.regions[regUp].fixUpperEdge)
                {
                    s.mesh.MeshDelete(s.mesh.Onext(e));
                    s.regions.ElementAt(regUp).fixUpperEdge = false;
                }
                s.mesh.MeshSplice(s.mesh.vertices[vEvent].anEdge, e);
                SweepEvent(ref s, vEvent);
                return;
            }
        }

        public static void ConnectLeftVertex(ref NativeTessState s, int vEvent)
        {
            int tmpReg = s.AllocRegion();
            s.regions.ElementAt(tmpReg).eUp = s.mesh.Sym(s.mesh.vertices[vEvent].anEdge);

            int foundNode = s.dict.Find(tmpReg, s);
            int regUp = s.dict.NodeKey(foundNode);
            s.FreeRegion(tmpReg);

            int regLo = s.RegionBelow(regUp);
            if (regLo == Undef) return;

            int eUp = s.regions[regUp].eUp;
            int eLo = s.regions[regLo].eUp;

            if (Geom.EdgeSign(s.mesh.vertices[s.mesh.Dst(eUp)], s.mesh.vertices[vEvent], s.mesh.vertices[s.mesh.Org(eUp)]) == 0.0f)
            {
                ConnectLeftDegenerate(ref s, regUp, vEvent);
                return;
            }

            int reg = Geom.VertLeq(s.mesh.vertices[s.mesh.Dst(eLo)], s.mesh.vertices[s.mesh.Dst(eUp)]) ? regUp : regLo;

            if (s.regions[regUp].inside || s.regions[reg].fixUpperEdge)
            {
                int eNew;
                if (reg == regUp)
                    eNew = s.mesh.Connect(s.mesh.Sym(s.mesh.vertices[vEvent].anEdge), s.mesh.Lnext(eUp));
                else
                    eNew = s.mesh.Sym(s.mesh.Connect(s.mesh.Dnext(eLo), s.mesh.vertices[vEvent].anEdge));

                if (s.regions[reg].fixUpperEdge)
                    FixUpperEdge(ref s, reg, eNew);
                else
                    ComputeWinding(ref s, AddRegionBelow(ref s, regUp, eNew));
                SweepEvent(ref s, vEvent);
            }
            else
            {
                AddRightEdges(ref s, regUp, s.mesh.vertices[vEvent].anEdge, s.mesh.vertices[vEvent].anEdge, Undef, true);
            }
        }

        public static void SweepEvent(ref NativeTessState s, int vEvent)
        {
            s.eventVertex = vEvent;

            int e = s.mesh.vertices[vEvent].anEdge;
            while (s.mesh.edges[e].activeRegion == Undef)
            {
                e = s.mesh.Onext(e);
                if (e == s.mesh.vertices[vEvent].anEdge)
                {
                    ConnectLeftVertex(ref s, vEvent);
                    return;
                }
            }

            int regUp = TopLeftRegion(ref s, s.mesh.edges[e].activeRegion);
            int reg = s.RegionBelow(regUp);
            int eTopLeft = s.regions[reg].eUp;
            int eBottomLeft = FinishLeftRegions(ref s, reg, Undef);

            if (s.mesh.Onext(eBottomLeft) == eTopLeft)
                ConnectRightVertex(ref s, regUp, eBottomLeft);
            else
                AddRightEdges(ref s, regUp, s.mesh.Onext(eBottomLeft), eTopLeft, eTopLeft, true);
        }

        public static void AddSentinel(ref NativeTessState s, float smin, float smax, float t)
        {
            int e = s.mesh.MeshMakeEdge();
            s.mesh.vertices.ElementAt(s.mesh.Org(e)).s = smax;
            s.mesh.vertices.ElementAt(s.mesh.Org(e)).t = t;
            s.mesh.vertices.ElementAt(s.mesh.Dst(e)).s = smin;
            s.mesh.vertices.ElementAt(s.mesh.Dst(e)).t = t;
            s.eventVertex = s.mesh.Dst(e);

            int reg = s.AllocRegion();
            s.regions.ElementAt(reg).eUp = e;
            s.regions.ElementAt(reg).windingNumber = 0;
            s.regions.ElementAt(reg).inside = false;
            s.regions.ElementAt(reg).fixUpperEdge = false;
            s.regions.ElementAt(reg).sentinel = true;
            s.regions.ElementAt(reg).dirty = false;
            s.regions.ElementAt(reg).nodeUp = s.dict.Insert(reg, s);
        }

        public static void InitEdgeDict(ref NativeTessState s)
        {
            s.dict.Reset();
            AddSentinel(ref s, -NativeTessState.SentinelCoord, NativeTessState.SentinelCoord, -NativeTessState.SentinelCoord);
            AddSentinel(ref s, -NativeTessState.SentinelCoord, NativeTessState.SentinelCoord, +NativeTessState.SentinelCoord);
        }

        public static void DoneEdgeDict(ref NativeTessState s)
        {
            while (true)
            {
                int minNode = s.dict.Min();
                int reg = s.dict.NodeKey(minNode);
                if (reg == Undef) break;
                DeleteRegion(ref s, reg);
            }
            s.dict.Reset();
        }

        public static void RemoveDegenerateEdges(ref NativeTessState s)
        {
            int eHead = s.mesh.eHead;
            int e = s.mesh.edges[eHead].next;
            while (e != eHead)
            {
                int eNext = s.mesh.edges[e].next;
                int eLnext = s.mesh.Lnext(e);

                if (Geom.VertEq(s.mesh.vertices[s.mesh.Org(e)], s.mesh.vertices[s.mesh.Dst(e)]) && s.mesh.Lnext(eLnext) != e)
                {
                    SpliceMergeVertices(ref s, eLnext, e);
                    s.mesh.MeshDelete(e);
                    e = eLnext;
                    eLnext = s.mesh.Lnext(e);
                }
                if (s.mesh.Lnext(eLnext) == e)
                {
                    if (eLnext != e)
                    {
                        if (eLnext == eNext || eLnext == s.mesh.Sym(eNext))
                            eNext = s.mesh.edges[eNext].next;
                        s.mesh.MeshDelete(eLnext);
                    }
                    if (e == eNext || e == s.mesh.Sym(eNext))
                        eNext = s.mesh.edges[eNext].next;
                    s.mesh.MeshDelete(e);
                }
                e = eNext;
            }
        }

        public static void InitPriorityQ(ref NativeTessState s)
        {
            int vHead = s.mesh.vHead;
            int v = s.mesh.vertices[vHead].next;
            while (v != vHead)
            {
                var pqh = s.pq.Insert(v, ref s.mesh.vertices);
                s.mesh.vertices.ElementAt(v).pqHandle = pqh;
                v = s.mesh.vertices[v].next;
            }
            s.pq.Init(ref s.mesh.vertices);
        }

        public static void RemoveDegenerateFaces(ref NativeTessState s)
        {
            int fHead = s.mesh.fHead;
            int f = s.mesh.faces[fHead].next;
            while (f != fHead)
            {
                int fNext = s.mesh.faces[f].next;
                int e = s.mesh.faces[f].anEdge;
                if (s.mesh.Lnext(s.mesh.Lnext(e)) == e)
                {
                    s.mesh.AddWinding(s.mesh.Onext(e), e);
                    s.mesh.MeshDelete(e);
                }
                f = fNext;
            }
        }

        public static void ComputeInterior(ref NativeTessState s)
        {
            RemoveDegenerateEdges(ref s);
            InitPriorityQ(ref s);
            RemoveDegenerateFaces(ref s);
            InitEdgeDict(ref s);

            while (true)
            {
                int v = s.pq.ExtractMin(ref s.mesh.vertices);
                if (v == Undef) break;

                while (true)
                {
                    if (s.pq.IsEmpty(ref s.mesh.vertices)) break;
                    int vNext = s.pq.Minimum(ref s.mesh.vertices);
                    if (vNext == Undef || !Geom.VertEq(s.mesh.vertices[vNext], s.mesh.vertices[v]))
                        break;

                    vNext = s.pq.ExtractMin(ref s.mesh.vertices);
                    SpliceMergeVertices(ref s, s.mesh.vertices[v].anEdge, s.mesh.vertices[vNext].anEdge);
                }
                SweepEvent(ref s, v);
            }
            DoneEdgeDict(ref s);
            RemoveDegenerateFaces(ref s);
        }

        public static void TessellateMonoRegion(ref NativeTessState s, int face)
        {
            int up = s.mesh.faces[face].anEdge;
            while (Geom.VertLeq(s.mesh.vertices[s.mesh.Dst(up)], s.mesh.vertices[s.mesh.Org(up)]))
            {
                up = s.mesh.Lprev(up);
            }
            while (Geom.VertLeq(s.mesh.vertices[s.mesh.Org(up)], s.mesh.vertices[s.mesh.Dst(up)]))
            {
                up = s.mesh.Lnext(up);
            }

            int lo = s.mesh.Lprev(up);

            while (s.mesh.Lnext(up) != lo)
            {
                if (Geom.VertLeq(s.mesh.vertices[s.mesh.Dst(up)], s.mesh.vertices[s.mesh.Org(lo)]))
                {
                    while (s.mesh.Lnext(lo) != up &&
                        (Geom.VertLeq(s.mesh.vertices[s.mesh.Dst(s.mesh.Lnext(lo))], s.mesh.vertices[s.mesh.Org(s.mesh.Lnext(lo))])
                        || Geom.EdgeSign(s.mesh.vertices[s.mesh.Org(lo)], s.mesh.vertices[s.mesh.Dst(lo)], s.mesh.vertices[s.mesh.Dst(s.mesh.Lnext(lo))]) <= 0.0f))
                    {
                        lo = s.mesh.Sym(s.mesh.Connect(s.mesh.Lnext(lo), lo));
                    }
                    lo = s.mesh.Lprev(lo);
                }
                else
                {
                    while (s.mesh.Lnext(lo) != up &&
                        (Geom.VertLeq(s.mesh.vertices[s.mesh.Org(s.mesh.Lprev(up))], s.mesh.vertices[s.mesh.Dst(s.mesh.Lprev(up))])
                        || Geom.EdgeSign(s.mesh.vertices[s.mesh.Dst(up)], s.mesh.vertices[s.mesh.Org(up)], s.mesh.vertices[s.mesh.Org(s.mesh.Lprev(up))]) >= 0.0f))
                    {
                        up = s.mesh.Sym(s.mesh.Connect(up, s.mesh.Lprev(up)));
                    }
                    up = s.mesh.Lnext(up);
                }
            }

            while (s.mesh.Lnext(s.mesh.Lnext(lo)) != up)
            {
                lo = s.mesh.Sym(s.mesh.Connect(s.mesh.Lnext(lo), lo));
            }
        }

        public static void TessellateInterior(ref NativeTessState s)
        {
            int fHead = s.mesh.fHead;
            int f = s.mesh.faces[fHead].next;
            while (f != fHead)
            {
                int fNext = s.mesh.faces[f].next;
                if (s.mesh.faces[f].inside)
                    TessellateMonoRegion(ref s, f);
                f = fNext;
            }
        }
    }
}
