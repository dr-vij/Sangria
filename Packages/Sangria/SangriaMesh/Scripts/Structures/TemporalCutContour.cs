using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace ViJMeshTools
{
    public enum SegmentConnectionResult
    {
        NoConnection = 0,
        SegmentStartConnected = 1,
        SegmentEndConnected = 2,
    }

    public class TemporalCutContour
    {
        private LinkedList<float3> mContourVertices = new LinkedList<float3>();

        public TemporalCutContour(CutSegment segment)
        {
            mContourVertices.AddLast(segment.Start);
            mContourVertices.AddLast(segment.End);
        }

        public float3[] GetContourArray()
        {
            var ret = new float3[mContourVertices.Count];
            var current = mContourVertices.First;
            var counter = 0;
            while (current != null)
            {
                ret[counter++] = current.Value;
                current = current.Next;
            }
            return ret;
        }

        public SegmentConnectionResult TryConnectContourSegment(CutSegment segment, float distanceTolerance = 1e-5f)
        {
            //Check if segment end can be connected to contour start/end
            var tolerance = distanceTolerance * distanceTolerance;
            if (math.distancesq(segment.End, mContourVertices.First.Value) < tolerance)
            {
                mContourVertices.AddFirst(segment.Start);
                return SegmentConnectionResult.SegmentEndConnected;
            }
            if (math.distancesq(segment.End, mContourVertices.Last.Value) < tolerance)
            {
                mContourVertices.AddLast(segment.Start);
                return SegmentConnectionResult.SegmentEndConnected;
            }

            //Check if segment start can be connected to contour start/end
            if (math.distancesq(segment.Start, mContourVertices.First.Value) < tolerance)
            {
                mContourVertices.AddFirst(segment.End);
                return SegmentConnectionResult.SegmentStartConnected;
            }
            if (math.distancesq(segment.Start, mContourVertices.Last.Value) < tolerance)
            {
                mContourVertices.AddLast(segment.End);
                return SegmentConnectionResult.SegmentStartConnected;
            }

            return SegmentConnectionResult.NoConnection;
        }
    }
}