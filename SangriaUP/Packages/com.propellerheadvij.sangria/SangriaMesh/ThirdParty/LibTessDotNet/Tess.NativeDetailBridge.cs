/*
** SGI FREE SOFTWARE LICENSE B (Version 2.0, Sept. 18, 2008)
** Copyright (C) 2011 Silicon Graphics, Inc.
** All Rights Reserved.
**
** Permission is hereby granted, free of charge, to any person obtaining a copy
** of this software and associated documentation files (the "Software"), to deal
** in the Software without restriction, including without limitation the rights
** to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies
** of the Software, and to permit persons to whom the Software is furnished to do so,
** subject to the following conditions:
**
** The above copyright notice including the dates of first publication and either this
** permission notice or a reference to http://oss.sgi.com/projects/FreeB/ shall be
** included in all copies or substantial portions of the Software.
**
** THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
** INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
** PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL SILICON GRAPHICS, INC.
** BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
** TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE
** OR OTHER DEALINGS IN THE SOFTWARE.
**
** Except as contained in this notice, the name of Silicon Graphics, Inc. shall not
** be used in advertising or otherwise to promote the sale, use or other dealings in
** this Software without prior written authorization from Silicon Graphics, Inc.
*/
/*
** Original Author: Eric Veach, July 1994.
** libtess2: Mikko Mononen, http://code.google.com/p/libtess2/.
** LibTessDotNet: Remi Gillig, https://github.com/speps/LibTessDotNet
**
** SangriaMesh native-output bridge:
** This partial keeps LibTess' sweep/monotone logic but bypasses managed contour/output arrays,
** emitting directly into SangriaMesh.NativeDetail dense triangle topology.
*/

using System;
using SangriaMesh;
using Unity.Mathematics;

#if DOUBLE
using Real = System.Double;
namespace LibTessDotNet.Double
#else
using Real = System.Single;
namespace LibTessDotNet
#endif
{
    public partial class Tess
    {
        public CoreResult TessellateNative(
            in NativeContourSet contours,
            ref NativeDetail output,
            TriangulationOptions options)
        {
            _normal = new Vec3(options.Normal.x, options.Normal.y, options.Normal.z);
            _vertices = null;
            _elements = null;
            _vertexCount = 0;
            _elementCount = 0;
            _combineCallback = null;
            _windingRule = MapWindingRule(options.WindingRule);
            NoEmptyPolygons = options.RemoveEmptyPolygons;

            ContourOrientation contourOrientation = MapContourOrientation(options.ContourOrientation);
            for (int contourIndex = 0; contourIndex < contours.ContourCount; contourIndex++)
                AddNativeContourInternal(in contours, contourIndex, contourOrientation);

            if (_mesh == null)
                return CoreResult.Success;

            try
            {
                ProjectPolygon();
                ComputeInterior();
                TessellateInterior();
                _mesh.Check();
                return EmitToNativeDetail(ref output);
            }
            finally
            {
                if (_mesh != null)
                    _pool.Return(ref _mesh);

                _vertices = null;
                _elements = null;
                _vertexCount = 0;
                _elementCount = 0;
                _combineCallback = null;
            }
        }

        private void AddNativeContourInternal(
            in NativeContourSet contours,
            int contourIndex,
            ContourOrientation forceOrientation)
        {
            int start = contours.ContourOffsets[contourIndex];
            int end = contours.ContourOffsets[contourIndex + 1];
            int count = end - start;
            if (count < 3)
                return;

            if (_mesh == null)
                _mesh = _pool.Get<Mesh>();

            bool reverse = false;
            if (forceOrientation != ContourOrientation.Original)
            {
                Real area = SignedArea(in contours, start, count);
                reverse =
                    (forceOrientation == ContourOrientation.Clockwise && area < 0.0f) ||
                    (forceOrientation == ContourOrientation.CounterClockwise && area > 0.0f);
            }

            MeshUtils.Edge e = null;
            for (int i = 0; i < count; ++i)
            {
                if (e == null)
                {
                    e = _mesh.MakeEdge(_pool);
                    _mesh.Splice(_pool, e, e._Sym);
                }
                else
                {
                    _mesh.SplitEdge(_pool, e);
                    e = e._Lnext;
                }

                int contourOffset = reverse ? count - 1 - i : i;
                int pointIndex = contours.ContourPointIndices[start + contourOffset];
                float3 position = contours.Positions[pointIndex];

                e._Org._coords = new Vec3(position.x, position.y, position.z);
                e._Org._data = null;
                e._winding = 1;
                e._Sym._winding = -1;
            }
        }

        private Real SignedArea(in NativeContourSet contours, int start, int count)
        {
            Real area = 0.0f;

            for (int i = 0; i < count; i++)
            {
                float3 v0 = contours.Positions[contours.ContourPointIndices[start + i]];
                float3 v1 = contours.Positions[contours.ContourPointIndices[start + ((i + 1) % count)]];

                area += (Real)(v0.x * v1.y);
                area -= (Real)(v0.y * v1.x);
            }

            return 0.5f * area;
        }

        private unsafe CoreResult EmitToNativeDetail(ref NativeDetail output)
        {
            MeshUtils.Vertex v;
            MeshUtils.Face f;
            MeshUtils.Edge edge;

            int pointCount = 0;
            int primitiveCount = 0;

            for (v = _mesh._vHead._next; v != _mesh._vHead; v = v._next)
                v._n = Undef;

            for (f = _mesh._fHead._next; f != _mesh._fHead; f = f._next)
            {
                if (!f._inside)
                    continue;

                if (NoEmptyPolygons)
                {
                    Real area = MeshUtils.FaceArea(f);
                    if (Math.Abs(area) < Real.Epsilon)
                        continue;
                }

                edge = f._anEdge;
                int faceVerts = 0;
                do
                {
                    v = edge._Org;
                    if (v._n == Undef)
                    {
                        v._n = pointCount;
                        pointCount++;
                    }

                    faceVerts++;
                    edge = edge._Lnext;
                } while (edge != f._anEdge);

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
                throw new InvalidOperationException("Point position attribute is required for triangulation output.");

            int* vertexToPoint = output.GetVertexToPointPointerUnchecked();
            int* primitiveTriangleData = output.GetPrimitiveTriangleDataPointerUnchecked();

            for (v = _mesh._vHead._next; v != _mesh._vHead; v = v._next)
            {
                if (v._n == Undef)
                    continue;

                pointPositions[v._n] = new float3((float)v._coords.X, (float)v._coords.Y, (float)v._coords.Z);
                vertexToPoint[v._n] = v._n;
            }

            int primitiveCursor = 0;
            for (f = _mesh._fHead._next; f != _mesh._fHead; f = f._next)
            {
                if (!f._inside)
                    continue;

                if (NoEmptyPolygons)
                {
                    Real area = MeshUtils.FaceArea(f);
                    if (Math.Abs(area) < Real.Epsilon)
                        continue;
                }

                edge = f._anEdge;
                int firstVertex = edge._Org._n;
                edge = edge._Lnext;
                int prevVertex = edge._Org._n;
                edge = edge._Lnext;

                while (edge != f._anEdge)
                {
                    primitiveTriangleData[primitiveCursor * 3] = firstVertex;
                    primitiveTriangleData[primitiveCursor * 3 + 1] = prevVertex;
                    primitiveTriangleData[primitiveCursor * 3 + 2] = edge._Org._n;
                    primitiveCursor++;

                    prevVertex = edge._Org._n;
                    edge = edge._Lnext;
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

        private static ContourOrientation MapContourOrientation(TriangulationContourOrientation orientation)
        {
            return orientation switch
            {
                TriangulationContourOrientation.Original => ContourOrientation.Original,
                TriangulationContourOrientation.Clockwise => ContourOrientation.Clockwise,
                TriangulationContourOrientation.CounterClockwise => ContourOrientation.CounterClockwise,
                _ => ContourOrientation.Original
            };
        }
    }
}
