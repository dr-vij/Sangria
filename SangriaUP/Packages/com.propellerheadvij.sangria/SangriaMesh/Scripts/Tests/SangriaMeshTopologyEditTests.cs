using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class SangriaMeshTopologyEditTests
{
    [Test]
    public void VertexFailPolicyBlocksRemovalWhenIncidentPrimitivesExist()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));
            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);

            using var tri = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
            int primitive = detail.AddPrimitive(tri);

            Assert.IsFalse(detail.CanRemoveVertex(v0, VertexDeletePolicy.FailIfIncidentPrimitivesExist));
            Assert.IsFalse(detail.RemoveVertex(v0, VertexDeletePolicy.FailIfIncidentPrimitivesExist));
            Assert.IsTrue(detail.IsVertexAlive(v0));
            Assert.IsTrue(detail.IsPrimitiveAlive(primitive));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RemoveFromIncidentPrimitivesKeepsPolygonWhenStillValid()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(1f, 1f, 0f));
            int p3 = detail.AddPoint(new float3(0f, 1f, 0f));
            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);

            using var quad = new NativeArray<int>(new[] { v0, v1, v2, v3 }, Allocator.Temp);
            int primitive = detail.AddPrimitive(quad);

            Assert.IsTrue(detail.RemoveVertex(v0, VertexDeletePolicy.RemoveFromIncidentPrimitives));
            Assert.IsFalse(detail.IsVertexAlive(v0));
            Assert.IsTrue(detail.IsPrimitiveAlive(primitive));
            Assert.AreEqual(3, detail.GetPrimitiveVertexCount(primitive));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void DeleteIncidentPrimitivesPolicyRemovesAllConnectedFaces()
    {
        var detail = new NativeDetail(16, Allocator.Temp);
        try
        {
            int pShared = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));
            int p3 = detail.AddPoint(new float3(-1f, 0f, 0f));
            int p4 = detail.AddPoint(new float3(0f, -1f, 0f));

            int vShared = detail.AddVertex(pShared);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);
            int v4 = detail.AddVertex(p4);

            using var triA = new NativeArray<int>(new[] { vShared, v1, v2 }, Allocator.Temp);
            using var triB = new NativeArray<int>(new[] { vShared, v3, v4 }, Allocator.Temp);
            int primitiveA = detail.AddPrimitive(triA);
            int primitiveB = detail.AddPrimitive(triB);

            Assert.IsTrue(detail.RemoveVertex(vShared, VertexDeletePolicy.DeleteIncidentPrimitives));
            Assert.IsFalse(detail.IsVertexAlive(vShared));
            Assert.IsFalse(detail.IsPrimitiveAlive(primitiveA));
            Assert.IsFalse(detail.IsPrimitiveAlive(primitiveB));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void PointPoliciesRespectIncidentChecks()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            int sharedPoint = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));

            int sharedVertex = detail.AddVertex(sharedPoint);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);

            using var tri = new NativeArray<int>(new[] { sharedVertex, v1, v2 }, Allocator.Temp);
            int primitive = detail.AddPrimitive(tri);
            Assert.GreaterOrEqual(primitive, 0);

            Assert.IsFalse(detail.CanRemovePoint(sharedPoint, PointDeletePolicy.FailIfIncidentVerticesExist));
            Assert.IsFalse(detail.RemovePoint(sharedPoint, PointDeletePolicy.FailIfIncidentVerticesExist));
            Assert.IsTrue(detail.IsPointAlive(sharedPoint));

            Assert.IsFalse(detail.CanRemovePoint(
                sharedPoint,
                PointDeletePolicy.DeleteIncidentVertices,
                VertexDeletePolicy.FailIfIncidentPrimitivesExist));
            Assert.IsFalse(detail.RemovePoint(
                sharedPoint,
                PointDeletePolicy.DeleteIncidentVertices,
                VertexDeletePolicy.FailIfIncidentPrimitivesExist));
            Assert.IsTrue(detail.IsPointAlive(sharedPoint));

            Assert.IsTrue(detail.RemovePoint(
                sharedPoint,
                PointDeletePolicy.DeleteIncidentVertices,
                VertexDeletePolicy.DeleteIncidentPrimitives));
            Assert.IsFalse(detail.IsPointAlive(sharedPoint));
            Assert.IsFalse(detail.IsPrimitiveAlive(primitive));
        }
        finally
        {
            detail.Dispose();
        }
    }

}
