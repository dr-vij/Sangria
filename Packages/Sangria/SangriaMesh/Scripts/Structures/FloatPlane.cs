using System.Collections;
using System.Collections.Generic;
using System.Security.AccessControl;
using Unity.Mathematics;
using UnityEngine;

namespace ViJMeshTools
{
    public enum LineToPlaneIntersection
    {
        LineIntersectionPoint,
        SegmentIntersectionPoint,
        LineOnPlane,
        NoIntersections,
    }

    //Some theory behind the planes/
    //http://geomalgorithms.com/a04-_planes.html
    public struct FloatPlane
    {
        /// <summary>
        /// Plane is A*x + B*y + C*z + D = 0;
        /// Plane normal. ABC of equation
        /// </summary>
        public float3 PlaneNormal { get; private set; }

        /// <summary>
        /// Plane signed distance from zero coords, D of equation
        /// </summary>
        public float SignedDistance { get; private set; }

        /// <summary>
        /// Initialize plane with plane direction and signed distance. if direction is incorrect it will create math.up() plane normal
        /// </summary>
        /// <param name="planeDirection"></param>
        /// <param name="signedDistance"></param>
        public FloatPlane(float3 planeDirection, float signedDistance)
        {
            PlaneNormal = math.normalizesafe(planeDirection, math.up());
            SignedDistance = signedDistance;
        }

        /// <summary>
        /// Initialize plane with 3 float3 points
        /// </summary>
        /// <param name="pointA"></param>
        /// <param name="pointB"></param>
        /// <param name="pointC"></param>
        public FloatPlane(float3 pointA, float3 pointB, float3 pointC)
        {
            //https://www.geeksforgeeks.org/program-to-find-equation-of-a-plane-passing-through-3-points/
            float3 abc1 = pointB - pointA;
            float3 abc2 = pointC - pointA;
            float3 abc = new float3(abc1.y * abc2.z - abc2.y * abc1.z, abc2.x * abc1.z - abc1.x * abc2.z, abc1.x * abc2.y - abc1.y * abc2.x);
            //normal vector (normalized ABC)
            PlaneNormal = math.normalize(abc);
            SignedDistance = -PlaneNormal.x * pointA.x - PlaneNormal.y * pointA.y - PlaneNormal.z * pointA.z;
        }

        /// <summary>
        /// Initialize plane with plane direction and point. if direction is incorrect it will create math.up() plane normal
        /// </summary>
        /// <param name="planeDirection"></param>
        /// <param name="point"></param>
        public FloatPlane(float3 planeDirection, float3 point)
        {
            PlaneNormal = math.normalizesafe(planeDirection, math.up());
            SignedDistance = -PlaneNormal.x * point.x - PlaneNormal.y * point.y - PlaneNormal.z * point.z;
        }

        /// <summary>
        /// Find intersection between line and this plane. Returns Line/Segment intersection and its coord if exists
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        public LineToPlaneIntersection IntersectWithLine(float3 x, float3 y, out float3 result)
        {
            var a = -SignedDistance * PlaneNormal;
            var v = a - x;
            var w = y - x;

            //approximation to plane along normal
            var d = math.dot(PlaneNormal, v);
            var e = math.dot(PlaneNormal, w);

            if (e != 0)
            {
                result = x + w * d / e;
                if (math.dot(x - result, y - result) <= 0)
                    return LineToPlaneIntersection.SegmentIntersectionPoint; //Intersection with segment
                else
                    return LineToPlaneIntersection.LineIntersectionPoint;    //Intersection with infinite line
            }
            else if (d == 0)
            {
                result = (x + y) / 2;
                return LineToPlaneIntersection.LineOnPlane;
            }
            else
            {
                result = new float3(0);
                return LineToPlaneIntersection.NoIntersections;
            }
        }

        /// <summary>
        /// Signed distance to plane is (A*x + B*y + C*z + D) / sqrt(A*A + B*B + C*C), where ABCD is plane function koeffs. xyz is point coords.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public float SignedDistanceToPoint(float3 point) => PlaneNormal.x * point.x + PlaneNormal.y * point.y + PlaneNormal.z * point.z + SignedDistance;

        /// <summary>
        /// Checks the side of the plane and point. positive side if zero or more
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public bool Sign(float3 point) => SignedDistanceToPoint(point) > 0;

        public FloatPlane GetTransformedPlane(float4x4 trsMatrix)
        {
            var transformedPoint = math.mul(trsMatrix, new float4(-SignedDistance * PlaneNormal, 1)).xyz;
            var normal = math.mul(trsMatrix, new float4(PlaneNormal, 0)).xyz;
            return new FloatPlane(normal, transformedPoint);
        }
    }

    public static class FloatPlaneHelpers
    {
        public static FloatPlane ToFloatPlane(this Plane unityPlane) => new FloatPlane(unityPlane.normal, unityPlane.distance);

        public static Plane ToUnityPlane(this FloatPlane floatPlane) => new Plane(floatPlane.PlaneNormal, floatPlane.SignedDistance);
    }
}
