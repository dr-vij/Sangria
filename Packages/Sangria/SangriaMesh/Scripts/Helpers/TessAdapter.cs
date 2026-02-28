using LibTessDotNet;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Diagnostics;
using Supercluster.KDTree;
using System;

namespace ViJMeshTools
{
    public struct TesseleationResult : IDisposable
    {
        /// <summary>
        /// Indicies for triangle of the tesselated mesh
        /// </summary>
        public NativeArray<int> Indicies;

        /// <summary>
        /// Verticies for triangle of the tesselated mesh
        /// </summary>
        public NativeArray<float3> Vertices;

        /// <summary>
        /// UVs for vertices of the tesselated mesh
        /// </summary>
        public NativeArray<float2> UVs;

        /// <summary>
        /// The normal of the plane of the tesselated mesh
        /// </summary>
        public float3 Normal;

        public void Dispose()
        {
            Indicies.Dispose();
            Vertices.Dispose();
            UVs.Dispose();
        }
    }

    public static class TessAdapter
    {
        public static TesseleationResult CreateContours(NativeHashSet<CutSegment> rawNativeHashSet, float3 planeUp, float3 planeRight, float3 planeNormal)
        {
            var contours = ConsolidateContoursFromSegmentsBruteforce(rawNativeHashSet.ToHashSet());
            var contourVertexList = contours.ToContourVertex();
            var tess = new Tess(null);
            foreach (var contourVertex in contourVertexList)
                tess.AddContour(contourVertex);
            tess.Tessellate(windingRule: WindingRule.EvenOdd, elementType: ElementType.Polygons, polySize: 3, combineCallback: null, normal: default);

            var indicies = new NativeArray<int>(tess.Elements.Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < indicies.Length; i++)
                indicies[i] = (int)tess.Elements[i];

            var ret = new TesseleationResult()
            {
                Indicies = indicies,
                Vertices = new NativeArray<float3>(tess.Vertices.Select(c => c.Position.ToFloat3()).ToArray(), Allocator.Persistent),
                UVs = new NativeArray<float2>(tess.Vertices.Select(c => new float2(math.dot(c.Position.ToFloat3(), planeUp), math.dot(c.Position.ToFloat3(), planeRight))).ToArray(), Allocator.Persistent),
                Normal = -planeNormal,
            };

            return ret;
        }

        private static Func<float[], float[], double> L2Norm_Squared_Float = (x, y) =>
        {
            float dist = 0f;
            for (int i = 0; i < x.Length; i++)
                dist += (x[i] - y[i]) * (x[i] - y[i]);

            return dist;
        };

        /// <summary>
        /// We take all edges and find closed segments in them
        /// This method uses KDTree. But unfortunately it is not so fast
        /// IMPORTANT MOMENT: HASHSET WILL BE MODIFIED
        /// </summary>
        /// <param name="rawSegments"></param>
        /// <returns></returns>
        public static List<float3[]> ConsolidateContoursFromSegmentsKDTree(HashSet<CutSegment> rawSegments)
        {
            var counter = 0;
            var nodesCount = rawSegments.Count * 2;
            var nodes = new CutSegment[nodesCount];
            var points = new float[nodesCount][];
            foreach (var segment in rawSegments)
            {
                nodes[counter] = segment;
                points[counter] = new float[3] { segment.Start.x, segment.Start.y, segment.Start.z };
                counter++;
                nodes[counter] = segment;
                points[counter] = new float[3] { segment.End.x, segment.End.y, segment.End.z };
                counter++;
            }
            var tree = new KDTree<float, CutSegment>(3, points, nodes, L2Norm_Squared_Float, float.MinValue, float.MaxValue);

            var segmentsToRemove = new HashSet<CutSegment>();
            var doneSegments = new HashSet<CutSegment>();
            var tempContours = new List<TemporalCutContour>();
            var point = new float[3];

            //We create new contours untill we have not used all segments
            while (rawSegments.Count != 0)
            {
                segmentsToRemove.Clear();
                var currentSegment = rawSegments.First();
                segmentsToRemove.Add(currentSegment);
                doneSegments.Add(currentSegment);
                var contour = new TemporalCutContour(currentSegment);
                tempContours.Add(contour);
                var successfullSearch = false;
                var isUsingStart = true;

                //try to connect elements to contour untill we can
                do
                {
                    successfullSearch = false;
                    var segmentPoint = isUsingStart ? currentSegment.Start : currentSegment.End;
                    point[0] = segmentPoint.x;
                    point[1] = segmentPoint.y;
                    point[2] = segmentPoint.z;

                    var searchResult = tree.NearestNeighbors(point, 2);

                    foreach (var nearestResult in searchResult)
                    {
                        var resultSegment = nearestResult.Item2;
                        if (doneSegments.Contains(resultSegment))
                            continue;

                        var connectionResult = contour.TryConnectContourSegment(resultSegment, 1e-05f);
                        if (connectionResult != SegmentConnectionResult.NoConnection)
                        {
                            isUsingStart = connectionResult == SegmentConnectionResult.SegmentEndConnected;
                            successfullSearch = true;
                            segmentsToRemove.Add(resultSegment);
                            doneSegments.Add(resultSegment);
                            currentSegment = resultSegment;
                            break;
                        }
                    }
                }
                while (successfullSearch);

                foreach (var segmentToRemove in segmentsToRemove)
                    rawSegments.Remove(segmentToRemove);
            }

            var contours = new List<float3[]>();
            foreach (var tempContour in tempContours)
                contours.Add(tempContour.GetContourArray());
            return contours;
        }

        /// <summary>
        /// Takes all segments and creates closed contours from it.
        /// IMPORTANTE NOTE: HASHSET WILL BE MODIFIED
        /// </summary>
        /// <param name="rawSegments"></param>
        /// <returns></returns>
        public static List<float3[]> ConsolidateContoursFromSegmentsBruteforce(HashSet<CutSegment> rawSegments)
        {
            var contours = new List<float3[]>();
            var listToClear = new List<CutSegment>();
            TemporalCutContour tempcontour = null;
            while (rawSegments.Count != 0)
            {
                var successfullIteration = false;
                do
                {
                    listToClear.Clear();
                    successfullIteration = false;
                    foreach (var currentSegment in rawSegments)
                    {
                        bool wasConnected;
                        if (tempcontour == null)
                        {
                            tempcontour = new TemporalCutContour(currentSegment);
                            wasConnected = true;
                        }
                        else
                        {
                            wasConnected = tempcontour.TryConnectContourSegment(currentSegment) != SegmentConnectionResult.NoConnection;
                        }

                        if (wasConnected)
                        {
                            listToClear.Add(currentSegment);
                            successfullIteration |= true;
                        }
                    }

                    for (int i = 0; i < listToClear.Count; i++)
                        rawSegments.Remove(listToClear[i]);
                }
                while (successfullIteration);

                contours.Add(tempcontour.GetContourArray());
                tempcontour = null;
            }
            return contours;
        }

        #region LibTessDotNetConverters
        public static float3 ToFloat3(this Vec3 vec3) => new float3(vec3.X, vec3.Y, vec3.Z);

        public static List<ContourVertex[]> ToContourVertex(this List<float3[]> initialList)
        {
            var ret = new List<ContourVertex[]>(initialList.Count);
            foreach (var initialArr in initialList)
            {
                var contourVertexArr = new ContourVertex[initialArr.Length];
                for (int i = 0; i < initialArr.Length; i++)
                {
                    var curInitialArrElm = initialArr[i];
                    contourVertexArr[i] = new ContourVertex(new Vec3(curInitialArrElm.x, curInitialArrElm.y, curInitialArrElm.z));
                }
                ret.Add(contourVertexArr);
            };
            return ret;
        }
        #endregion
    }
}