using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class SangriaMeshAdjacencyTests
{
    [Test]
    public void RemovingPointWithMultipleIncidentVerticesInSamePolygonKeepsAdjacencyConsistent()
    {
        var detail = new NativeDetail(16, Allocator.Temp);
        try
        {
            int sharedPoint = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));
            int p3 = detail.AddPoint(new float3(-1f, 0f, 0f));

            int sharedVertexA = detail.AddVertex(sharedPoint);
            int v1 = detail.AddVertex(p1);
            int sharedVertexB = detail.AddVertex(sharedPoint);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);

            using var polygon = new NativeArray<int>(new[] { sharedVertexA, v1, sharedVertexB, v2, v3 }, Allocator.Temp);
            int primitive = detail.AddPrimitive(polygon);
            Assert.GreaterOrEqual(primitive, 0);

            Assert.IsTrue(detail.RemovePoint(sharedPoint));

            Assert.IsFalse(detail.IsVertexAlive(sharedVertexA));
            Assert.IsFalse(detail.IsVertexAlive(sharedVertexB));
            Assert.IsTrue(detail.IsPrimitiveAlive(primitive));
            Assert.AreEqual(3, detail.GetPrimitiveVertexCount(primitive));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RemovingVertexAfterPrimitiveMutationKeepsAdjacencyConsistent()
    {
        var detail = new NativeDetail(16, Allocator.Temp);
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

            using var triA = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
            using var triB = new NativeArray<int>(new[] { v0, v2, v3 }, Allocator.Temp);

            int primitiveA = detail.AddPrimitive(triA);
            int primitiveB = detail.AddPrimitive(triB);

            Assert.IsTrue(detail.RemovePrimitive(primitiveA));
            Assert.IsTrue(detail.RemoveVertex(v1));
            Assert.IsTrue(detail.IsPrimitiveAlive(primitiveB));
            Assert.AreEqual(3, detail.GetPrimitiveVertexCount(primitiveB));

            Assert.IsTrue(detail.RemoveVertex(v0));
            Assert.IsFalse(detail.IsPrimitiveAlive(primitiveB));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RemovingPointWithMultipleVerticesRemovesAllIncidentPrimitives()
    {
        var detail = new NativeDetail(16, Allocator.Temp);
        try
        {
            int sharedPoint = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));
            int p3 = detail.AddPoint(new float3(-1f, 0f, 0f));
            int p4 = detail.AddPoint(new float3(0f, -1f, 0f));

            int sharedVertexA = detail.AddVertex(sharedPoint);
            int sharedVertexB = detail.AddVertex(sharedPoint);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);
            int v4 = detail.AddVertex(p4);

            using var triA = new NativeArray<int>(new[] { sharedVertexA, v1, v2 }, Allocator.Temp);
            using var triB = new NativeArray<int>(new[] { sharedVertexB, v3, v4 }, Allocator.Temp);

            int primitiveA = detail.AddPrimitive(triA);
            int primitiveB = detail.AddPrimitive(triB);

            Assert.IsTrue(detail.RemovePoint(sharedPoint));

            Assert.IsFalse(detail.IsVertexAlive(sharedVertexA));
            Assert.IsFalse(detail.IsVertexAlive(sharedVertexB));
            Assert.IsFalse(detail.IsPrimitiveAlive(primitiveA));
            Assert.IsFalse(detail.IsPrimitiveAlive(primitiveB));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RemovingVertexAfterDensePopulateKeepsTopologyConsistent()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            SangriaMeshSphereGenerator.PopulateUvSphere(ref detail, 0.5f, 8, 6);
            int initialVertexCount = detail.VertexCount;

            Assert.IsTrue(detail.IsVertexAlive(0));
            Assert.IsTrue(detail.RemoveVertex(0));
            Assert.AreEqual(initialVertexCount - 1, detail.VertexCount);

            var compiled = detail.Compile(Allocator.Temp);
            try
            {
                Assert.AreEqual(detail.VertexCount, compiled.VertexCount);
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void DeletePoliciesWorkOnDirtyAdjacencyCache()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            SangriaMeshSphereGenerator.PopulateUvSphere(ref detail, 0.5f, 12, 8);

            Assert.IsFalse(detail.CanRemoveVertex(0, VertexDeletePolicy.FailIfIncidentPrimitivesExist));
            Assert.IsTrue(detail.CanRemoveVertex(0, VertexDeletePolicy.RemoveFromIncidentPrimitives));
            Assert.IsFalse(detail.CanRemovePoint(0, PointDeletePolicy.FailIfIncidentVerticesExist));

            Assert.IsTrue(detail.RemoveVertex(0, VertexDeletePolicy.DeleteIncidentPrimitives));
            Assert.IsFalse(detail.IsVertexAlive(0));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RemovePointWithPoliciesWorksWhenAdjacencyIsDirty()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            SangriaMeshSphereGenerator.PopulateUvSphere(ref detail, 0.5f, 12, 8);

            Assert.IsFalse(detail.RemovePoint(0, PointDeletePolicy.FailIfIncidentVerticesExist));
            Assert.IsTrue(detail.RemovePoint(0, PointDeletePolicy.DeleteIncidentVertices, VertexDeletePolicy.DeleteIncidentPrimitives));
            Assert.IsFalse(detail.IsPointAlive(0));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RemovingSingleDuplicateReferenceKeepsIncidentTrackingCorrect()
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

            using var polygon = new NativeArray<int>(new[] { v0, v1, v0, v2 }, Allocator.Temp);
            int primitive = detail.AddPrimitive(polygon);
            Assert.GreaterOrEqual(primitive, 0);

            Assert.IsTrue(detail.RemoveVertexFromPrimitive(primitive, 2));
            Assert.IsTrue(detail.IsPrimitiveAlive(primitive));
            Assert.IsFalse(detail.CanRemoveVertex(v0, VertexDeletePolicy.FailIfIncidentPrimitivesExist));

            Assert.IsTrue(detail.RemoveVertexFromPrimitive(primitive, 0));
            Assert.IsFalse(detail.IsPrimitiveAlive(primitive));
            Assert.IsTrue(detail.CanRemoveVertex(v0, VertexDeletePolicy.FailIfIncidentPrimitivesExist));
        }
        finally
        {
            detail.Dispose();
        }
    }
}
