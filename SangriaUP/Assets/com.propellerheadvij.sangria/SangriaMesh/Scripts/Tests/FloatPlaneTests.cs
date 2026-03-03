using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace ViJMeshTools.Tests
{
    public class FloatPlaneTests
    {
        private int mTestCount = 10000;
        private float mDelta = 1e-5f;
        private Vector2 mVectorsMinMax = new Vector2(-10f, 10f);
        private Vector2 mFloatMinMax = new Vector2(-100f, 100f);

        [Test]
        public void LineIntersection()
        {
            for (int i = 0; i < mTestCount; i++)
            {
                //Intersect with perpendicular segment in the center of it
                var normal = math.normalize(GetRandomVector3());
                var signedDistance = GetRandomFloat();
                var lineOffsets = GetRandomFloat();
                var positivePoint = normal * -signedDistance + normal * lineOffsets;
                var negativePoint = normal * -signedDistance - normal * lineOffsets;
                var center = (positivePoint + negativePoint) / 2;

                var plane = new FloatPlane(normal, signedDistance);

                var isIntersected1 = plane.IntersectWithLine(positivePoint, negativePoint, out var intersectionPoint1);
                var isIntersected2 = plane.IntersectWithLine(negativePoint, positivePoint, out var intersectionPoint2);

                Assert.AreEqual(LineToPlaneIntersection.SegmentIntersectionPoint, isIntersected1);
                Assert.AreEqual(LineToPlaneIntersection.SegmentIntersectionPoint, isIntersected2);

                Assert.AreEqual(0, math.distance(center, intersectionPoint1), 1e-4f);
                Assert.AreEqual(0, math.distance(center, intersectionPoint2), 1e-4f);

                //Intersect with perpendicular line
                normal = math.normalize(GetRandomVector3());
                signedDistance = GetRandomFloat();
                lineOffsets = GetRandomFloat();
                var point1 = normal * -signedDistance + normal * lineOffsets;
                var point2 = normal * -signedDistance + normal * lineOffsets * 2;
                var correctIntersection = normal * -signedDistance;

                plane = new FloatPlane(normal, signedDistance);

                var isIntersected = plane.IntersectWithLine(point1, point2, out var intersectionPoint);
                Assert.AreEqual(LineToPlaneIntersection.LineIntersectionPoint, isIntersected);
                Assert.AreEqual(0, math.distance(correctIntersection, intersectionPoint), 1e-4f);
            }
        }

        [Test]
        public void PlaneNormalIsNormalized()
        {
            for (int i = 0; i < mTestCount; i++)
            {
                //Create Random direction and random points to create plane
                float3 floatDirection = GetRandomVector3(); ;
                float signedDistance = GetRandomFloat();

                //Create new plane in direction and signed distance from zero coords
                var floatPlane = new FloatPlane(floatDirection, signedDistance);
                Assert.AreEqual(1f, math.length(floatPlane.PlaneNormal), 1e-6);
            }
        }

        [Test]
        public void DistanceToPlaneTests()
        {
            for (int i = 0; i < mTestCount; i++)
            {
                //Create Random direction and random points to create plane
                Vector3 vectorDirecton = GetRandomVector3();
                float3 floatDirection = vectorDirecton;

                float signedDistance = GetRandomFloat();

                //Create new plane in direction and signed distance from zero coords
                var floatPlane = new FloatPlane(floatDirection, signedDistance);
                var unityPlane = new Plane(vectorDirecton, signedDistance);

                Vector3 vectorTestPoint = GetRandomVector3();
                float3 floatTestPoint = vectorTestPoint;

                Assert.AreEqual(unityPlane.GetDistanceToPoint(vectorTestPoint), floatPlane.SignedDistanceToPoint(floatTestPoint), 1e-3);
            }
        }

        [Test]
        public void CreatePlaneNormalAndSignedDistance()
        {
            for (int i = 0; i < mTestCount; i++)
            {
                //Create Random direction and random points to create plane
                Vector3 vectorDirecton = GetRandomVector3();
                float3 floatDirection = vectorDirecton;

                float signedDistance = GetRandomFloat();

                //Create new plane in direction and signed distance from zero coords
                var floatPlane = new FloatPlane(floatDirection, signedDistance);
                var unityPlane = new Plane(vectorDirecton, signedDistance);
                AssertComparePlanes(floatPlane, unityPlane);
            }
        }

        [Test]
        public void CreatePlaneNormalAndPoint()
        {
            for (int i = 0; i < mTestCount; i++)
            {
                Vector3 vectorDirecton = GetRandomVector3();
                float3 floatDirection = vectorDirecton;

                Vector3 vectorPoint = GetRandomVector3();
                float3 floatPoint = vectorPoint;

                //Create new plane in direction and point
                var floatPlane = new FloatPlane(floatDirection, floatPoint);
                var unityPlane = new Plane(vectorDirecton, vectorPoint);
                AssertComparePlanes(floatPlane, unityPlane);
            }
        }

        [Test]
        public void CreatePlane3Points()
        {
            for (int i = 0; i < mTestCount; i++)
            {
                Vector3 vp1 = GetRandomVector3();
                float3 fp1 = vp1;
                Vector3 vp2 = GetRandomVector3();
                float3 fp2 = vp2;
                Vector3 vp3 = GetRandomVector3();
                float3 fp3 = vp3;

                //Create new plane with 3 points
                var floatPlane = new FloatPlane(fp1, fp2, fp3);
                var unityPlane = new Plane(vp1, vp2, vp3);
                AssertComparePlanes(floatPlane, unityPlane);
            }
        }

        private Vector3 GetRandomVector3()
        {
            float min = mVectorsMinMax.x;
            float max = mVectorsMinMax.y;
            return new Vector3(UnityEngine.Random.Range(min, max), UnityEngine.Random.Range(min, max), UnityEngine.Random.Range(min, max));
        }

        private float GetRandomFloat()
        {
            return UnityEngine.Random.Range(mFloatMinMax.x, mFloatMinMax.y);
        }

        private void AssertComparePlanes(FloatPlane floatPlane, Plane unityPlane)
        {
            for (int i = 0; i < 3; i++)
                Assert.AreEqual(unityPlane.normal[i], floatPlane.PlaneNormal[i], mDelta);

            Assert.AreEqual(unityPlane.distance, floatPlane.SignedDistance, mDelta);
        }
    }
}
