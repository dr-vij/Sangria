using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace SangriaMesh.NativeTess
{
    internal static class NativeTessAPI
    {
        private const int Undef = -1;

        public static CoreResult Tessellate(
            in NativeContourSet contours,
            ref NativeDetail output,
            in TriangulationOptions options)
        {
            int totalVerts = contours.ContourPointIndices.Length;
            if (totalVerts == 0)
                return CoreResult.Success;

            var state = NativeTessState.Create(totalVerts, Allocator.Persistent);
            try
            {
                state.windingRule = MapWindingRule(options.WindingRule);
                state.normal = options.Normal;
                state.removeEmptyPolygons = options.RemoveEmptyPolygons;

                TriangulationContourOrientation orientation = options.ContourOrientation;

                for (int ci = 0; ci < contours.ContourCount; ci++)
                {
                    AddContour(ref state, in contours, ci, orientation);
                }
                ProjectPolygon(ref state);
                Sweep.ComputeInterior(ref state);
                Sweep.TessellateInterior(ref state);

                return EmitToNativeDetail(ref state, ref output);
            }
            finally
            {
                state.Dispose();
            }
        }

        private static void AddContour(ref NativeTessState s, in NativeContourSet contours, int contourIndex, TriangulationContourOrientation forceOrientation)
        {
            int start = contours.ContourOffsets[contourIndex];
            int end = contours.ContourOffsets[contourIndex + 1];
            int count = end - start;
            if (count < 3) return;

            bool reverse = false;
            if (forceOrientation != TriangulationContourOrientation.Original)
            {
                float area = SignedArea(in contours, start, count);
                reverse = (forceOrientation == TriangulationContourOrientation.Clockwise && area < 0.0f) ||
                          (forceOrientation == TriangulationContourOrientation.CounterClockwise && area > 0.0f);
            }

            int e = Undef;
            for (int i = 0; i < count; i++)
            {

                if (e == Undef)
                {
                    e = s.mesh.MeshMakeEdge();
                    s.mesh.MeshSplice(e, s.mesh.Sym(e));
                }
                else
                {
                    s.mesh.SplitEdge(e);
                    e = s.mesh.Lnext(e);
                }

                int contourOffset = reverse ? count - 1 - i : i;
                int pointIndex = contours.ContourPointIndices[start + contourOffset];
                float3 position = contours.Positions[pointIndex];

                s.mesh.vertices.ElementAt(s.mesh.Org(e)).coords = position;
                s.mesh.edges.ElementAt(e).winding = 1;
                s.mesh.edges.ElementAt(s.mesh.Sym(e)).winding = -1;
            }
        }

        private static float SignedArea(in NativeContourSet contours, int start, int count)
        {
            float area = 0.0f;
            for (int i = 0; i < count; i++)
            {
                float3 v0 = contours.Positions[contours.ContourPointIndices[start + i]];
                float3 v1 = contours.Positions[contours.ContourPointIndices[start + ((i + 1) % count)]];
                area += v0.x * v1.y;
                area -= v0.y * v1.x;
            }
            return 0.5f * area;
        }

        private static void ProjectPolygon(ref NativeTessState s)
        {
            float3 norm = s.normal;
            bool computedNormal = false;

            if (norm.x == 0.0f && norm.y == 0.0f && norm.z == 0.0f)
            {
                ComputeNormal(ref s, out norm);
                s.normal = norm;
                computedNormal = true;
            }

            int longAxis = LongAxis(norm);

            const float SUnitX = 1.0f;
            const float SUnitY = 0.0f;

            float3 sU = float3.zero;
            sU[(longAxis + 1) % 3] = SUnitX;
            sU[(longAxis + 2) % 3] = SUnitY;

            float3 tU = float3.zero;
            tU[(longAxis + 1) % 3] = norm[longAxis] > 0.0f ? -SUnitY : SUnitY;
            tU[(longAxis + 2) % 3] = norm[longAxis] > 0.0f ? SUnitX : -SUnitX;

            s.sUnit = sU;
            s.tUnit = tU;

            int vHead = s.mesh.vHead;
            for (int v = s.mesh.vertices[vHead].next; v != vHead; v = s.mesh.vertices[v].next)
            {
                s.mesh.vertices.ElementAt(v).s = math.dot(s.mesh.vertices[v].coords, sU);
                s.mesh.vertices.ElementAt(v).t = math.dot(s.mesh.vertices[v].coords, tU);
            }

            if (computedNormal)
                CheckOrientation(ref s);

            bool first = true;
            for (int v = s.mesh.vertices[vHead].next; v != vHead; v = s.mesh.vertices[v].next)
            {
                if (first)
                {
                    s.bminX = s.bmaxX = s.mesh.vertices[v].s;
                    s.bminY = s.bmaxY = s.mesh.vertices[v].t;
                    first = false;
                }
                else
                {
                    if (s.mesh.vertices[v].s < s.bminX) s.bminX = s.mesh.vertices[v].s;
                    if (s.mesh.vertices[v].s > s.bmaxX) s.bmaxX = s.mesh.vertices[v].s;
                    if (s.mesh.vertices[v].t < s.bminY) s.bminY = s.mesh.vertices[v].t;
                    if (s.mesh.vertices[v].t > s.bmaxY) s.bmaxY = s.mesh.vertices[v].t;
                }
            }
        }

        private static void ComputeNormal(ref NativeTessState s, out float3 norm)
        {
            int vHead = s.mesh.vHead;
            int vFirst = s.mesh.vertices[vHead].next;
            if (vFirst == vHead)
            {
                norm = new float3(0, 0, 1);
                return;
            }

            float3 minVal = s.mesh.vertices[vFirst].coords;
            float3 maxVal = s.mesh.vertices[vFirst].coords;
            int3 minVert = new int3(vFirst, vFirst, vFirst);
            int3 maxVert = new int3(vFirst, vFirst, vFirst);

            for (int v = s.mesh.vertices[vFirst].next; v != vHead; v = s.mesh.vertices[v].next)
            {
                float3 c = s.mesh.vertices[v].coords;
                if (c.x < minVal.x) { minVal.x = c.x; minVert.x = v; }
                if (c.y < minVal.y) { minVal.y = c.y; minVert.y = v; }
                if (c.z < minVal.z) { minVal.z = c.z; minVert.z = v; }
                if (c.x > maxVal.x) { maxVal.x = c.x; maxVert.x = v; }
                if (c.y > maxVal.y) { maxVal.y = c.y; maxVert.y = v; }
                if (c.z > maxVal.z) { maxVal.z = c.z; maxVert.z = v; }
            }

            int ii = 0;
            if (maxVal.y - minVal.y > maxVal.x - minVal.x) ii = 1;
            if (maxVal.z - minVal.z > maxVal[ii] - minVal[ii]) ii = 2;

            if (minVal[ii] >= maxVal[ii])
            {
                norm = new float3(0, 0, 1);
                return;
            }

            int v1 = ii == 0 ? minVert.x : (ii == 1 ? minVert.y : minVert.z);
            int v2 = ii == 0 ? maxVert.x : (ii == 1 ? maxVert.y : maxVert.z);
            float3 d1 = s.mesh.vertices[v1].coords - s.mesh.vertices[v2].coords;

            float maxLen2 = 0;
            norm = float3.zero;
            for (int v = s.mesh.vertices[vHead].next; v != vHead; v = s.mesh.vertices[v].next)
            {
                float3 d2 = s.mesh.vertices[v].coords - s.mesh.vertices[v2].coords;
                float3 tNorm = math.cross(d1, d2);
                float tLen2 = math.lengthsq(tNorm);
                if (tLen2 > maxLen2)
                {
                    maxLen2 = tLen2;
                    norm = tNorm;
                }
            }

            if (maxLen2 <= 0.0f)
            {
                norm = float3.zero;
                int la = LongAxis(d1);
                norm[la] = 1;
            }
        }

        private static void CheckOrientation(ref NativeTessState s)
        {
            float area = 0.0f;
            int fHead = s.mesh.fHead;
            for (int f = s.mesh.faces[fHead].next; f != fHead; f = s.mesh.faces[f].next)
            {
                int anEdge = s.mesh.faces[f].anEdge;
                if (s.mesh.edges[anEdge].winding <= 0) continue;
                area += Geom.FaceArea(ref s.mesh, f);
            }
            if (area < 0.0f)
            {
                int vHead = s.mesh.vHead;
                for (int v = s.mesh.vertices[vHead].next; v != vHead; v = s.mesh.vertices[v].next)
                {
                    s.mesh.vertices.ElementAt(v).t = -s.mesh.vertices[v].t;
                }
                s.tUnit = -s.tUnit;
            }
        }

        private static int LongAxis(float3 v)
        {
            int i = 0;
            if (math.abs(v.y) > math.abs(v.x)) i = 1;
            if (math.abs(v.z) > math.abs(i == 0 ? v.x : v.y)) i = 2;
            return i;
        }

        private static unsafe CoreResult EmitToNativeDetail(ref NativeTessState s, ref NativeDetail output)
        {
            int pointCount = 0;
            int primitiveCount = 0;

            int vHead = s.mesh.vHead;
            for (int v = s.mesh.vertices[vHead].next; v != vHead; v = s.mesh.vertices[v].next)
            {
                s.mesh.vertices.ElementAt(v).n = Undef;
            }

            int fHead = s.mesh.fHead;
            for (int f = s.mesh.faces[fHead].next; f != fHead; f = s.mesh.faces[f].next)
            {
                if (!s.mesh.faces[f].inside) continue;

                if (s.removeEmptyPolygons)
                {
                    float area = Geom.FaceArea(ref s.mesh, f);
                    if (math.abs(area) < float.Epsilon) continue;
                }

                int edge = s.mesh.faces[f].anEdge;
                int faceVerts = 0;
                int startE = edge;
                do
                {
                    int v = s.mesh.Org(edge);
                    if (s.mesh.vertices[v].n == Undef)
                    {
                        s.mesh.vertices.ElementAt(v).n = pointCount;
                        pointCount++;
                    }
                    faceVerts++;
                    edge = s.mesh.Lnext(edge);
                } while (edge != startE);

                if (faceVerts >= 3)
                    primitiveCount += faceVerts - 2;
            }

            if (pointCount <= 0 || primitiveCount <= 0)
                return CoreResult.Success;

            output.AllocateDenseTopologyUnchecked(
                pointCount,
                pointCount,
                primitiveCount,
                prepareTriangleStorage: true,
                initializeVertexToPoint: true);

            if (output.TryGetPointAccessor<float3>(AttributeID.Position, out var pointPositions) != CoreResult.Success)
                return CoreResult.InvalidOperation;

            int* vertexToPoint = output.GetVertexToPointPointerUnchecked();
            int* primitiveTriangleData = output.GetPrimitiveTriangleDataPointerUnchecked();

            for (int v = s.mesh.vertices[vHead].next; v != vHead; v = s.mesh.vertices[v].next)
            {
                if (s.mesh.vertices[v].n == Undef) continue;
                int n = s.mesh.vertices[v].n;
                pointPositions[n] = s.mesh.vertices[v].coords;
                vertexToPoint[n] = n;
            }

            int primitiveCursor = 0;
            for (int f = s.mesh.faces[fHead].next; f != fHead; f = s.mesh.faces[f].next)
            {
                if (!s.mesh.faces[f].inside) continue;

                if (s.removeEmptyPolygons)
                {
                    float area = Geom.FaceArea(ref s.mesh, f);
                    if (math.abs(area) < float.Epsilon) continue;
                }

                int edge = s.mesh.faces[f].anEdge;
                int firstVertex = s.mesh.vertices[s.mesh.Org(edge)].n;
                edge = s.mesh.Lnext(edge);
                int prevVertex = s.mesh.vertices[s.mesh.Org(edge)].n;
                edge = s.mesh.Lnext(edge);

                while (edge != s.mesh.faces[f].anEdge)
                {
                    primitiveTriangleData[primitiveCursor * 3] = firstVertex;
                    primitiveTriangleData[primitiveCursor * 3 + 1] = prevVertex;
                    primitiveTriangleData[primitiveCursor * 3 + 2] = s.mesh.vertices[s.mesh.Org(edge)].n;
                    primitiveCursor++;

                    prevVertex = s.mesh.vertices[s.mesh.Org(edge)].n;
                    edge = s.mesh.Lnext(edge);
                }
            }

            output.MarkTopologyAndAttributeChanged();
            return CoreResult.Success;
        }

        private static WindingRule MapWindingRule(TriangulationWindingRule rule)
        {
            return rule switch
            {
                TriangulationWindingRule.EvenOdd => WindingRule.EvenOdd,
                TriangulationWindingRule.NonZero => WindingRule.NonZero,
                TriangulationWindingRule.Positive => WindingRule.Positive,
                TriangulationWindingRule.Negative => WindingRule.Negative,
                TriangulationWindingRule.AbsGeqTwo => WindingRule.AbsGeqTwo,
                _ => WindingRule.EvenOdd
            };
        }
    }
}
