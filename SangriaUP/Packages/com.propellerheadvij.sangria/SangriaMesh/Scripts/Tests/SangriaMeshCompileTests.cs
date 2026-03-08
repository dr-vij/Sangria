using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class SangriaMeshCompileTests
{
    private const int VertexWeightAttribute = 8101;
    private const int PrimitiveTagAttribute = 8102;

    [Test]
    public void CompileSparseAfterDirtyVertexRemovalProducesValidTopologyIndices()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            SangriaMeshSphereGenerator.PopulateUvSphere(ref detail, 0.5f, 24, 16);
            Assert.IsTrue(detail.RemoveVertex(0, VertexDeletePolicy.RemoveFromIncidentPrimitives));

            var compiled = detail.Compile(Allocator.Temp);
            try
            {
                Assert.AreEqual(detail.PointCount, compiled.PointCount);
                Assert.AreEqual(detail.VertexCount, compiled.VertexCount);
                Assert.AreEqual(detail.PrimitiveCount, compiled.PrimitiveCount);

                for (int i = 0; i < compiled.VertexToPointDense.Length; i++)
                {
                    int densePoint = compiled.VertexToPointDense[i];
                    Assert.GreaterOrEqual(densePoint, 0);
                    Assert.Less(densePoint, compiled.PointCount);
                }

                for (int i = 0; i < compiled.PrimitiveVerticesDense.Length; i++)
                {
                    int denseVertex = compiled.PrimitiveVerticesDense[i];
                    Assert.GreaterOrEqual(denseVertex, 0);
                    Assert.Less(denseVertex, compiled.VertexCount);
                }

                for (int i = 0; i < compiled.PrimitiveCount; i++)
                {
                    int length = compiled.PrimitiveOffsetsDense[i + 1] - compiled.PrimitiveOffsetsDense[i];
                    Assert.GreaterOrEqual(length, 3);
                }
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
    public void CompileSparseRemapsPointIndicesAfterPointRemoval()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));
            int p3 = detail.AddPoint(new float3(-1f, 0f, 0f));

            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);

            using var quad = new NativeArray<int>(new[] { v0, v1, v2, v3 }, Allocator.Temp);
            int primitive = detail.AddPrimitive(quad);
            Assert.GreaterOrEqual(primitive, 0);

            Assert.IsTrue(detail.RemovePoint(
                p1,
                PointDeletePolicy.DeleteIncidentVertices,
                VertexDeletePolicy.RemoveFromIncidentPrimitives));

            var compiled = detail.Compile(Allocator.Temp);
            try
            {
                Assert.AreEqual(3, compiled.PointCount);
                Assert.AreEqual(3, compiled.VertexCount);
                Assert.AreEqual(1, compiled.PrimitiveCount);
                Assert.AreEqual(3, compiled.PrimitiveVerticesDense.Length);

                Assert.AreEqual(CoreResult.Success,
                    compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var pointPositions));
                Assert.AreEqual(new float3(0f, 0f, 0f), pointPositions[0]);
                Assert.AreEqual(new float3(0f, 1f, 0f), pointPositions[1]);
                Assert.AreEqual(new float3(-1f, 0f, 0f), pointPositions[2]);

                for (int i = 0; i < compiled.VertexToPointDense.Length; i++)
                {
                    int densePoint = compiled.VertexToPointDense[i];
                    Assert.GreaterOrEqual(densePoint, 0);
                    Assert.Less(densePoint, compiled.PointCount);
                }
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
    public void CompileSparseRemapsVertexAndPrimitiveAttributesAfterTopologyHoles()
    {
        var detail = new NativeDetail(8, Allocator.Temp);
        try
        {
            Assert.AreEqual(CoreResult.Success, detail.AddVertexAttribute<float>(VertexWeightAttribute));
            Assert.AreEqual(CoreResult.Success, detail.AddPrimitiveAttribute<int>(PrimitiveTagAttribute));

            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(1f, 1f, 0f));
            int p3 = detail.AddPoint(new float3(0f, 1f, 0f));
            int p4 = detail.AddPoint(new float3(2f, 0f, 0f));

            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);
            int v4 = detail.AddVertex(p4);

            Assert.AreEqual(CoreResult.Success, detail.TryGetVertexAttributeHandle<float>(VertexWeightAttribute, out var vertexWeightHandle));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v0, vertexWeightHandle, 10f));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v1, vertexWeightHandle, 11f));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v2, vertexWeightHandle, 12f));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v3, vertexWeightHandle, 13f));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v4, vertexWeightHandle, 14f));

            using var triA = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
            using var triB = new NativeArray<int>(new[] { v0, v2, v3 }, Allocator.Temp);
            using var triC = new NativeArray<int>(new[] { v1, v4, v2 }, Allocator.Temp);

            int primitiveA = detail.AddPrimitive(triA);
            int primitiveB = detail.AddPrimitive(triB);
            int primitiveC = detail.AddPrimitive(triC);
            Assert.GreaterOrEqual(primitiveA, 0);
            Assert.GreaterOrEqual(primitiveB, 0);
            Assert.GreaterOrEqual(primitiveC, 0);

            Assert.AreEqual(CoreResult.Success, detail.TryGetPrimitiveAttributeHandle<int>(PrimitiveTagAttribute, out var primitiveTagHandle));
            Assert.AreEqual(CoreResult.Success, detail.TrySetPrimitiveAttribute(primitiveA, primitiveTagHandle, 100));
            Assert.AreEqual(CoreResult.Success, detail.TrySetPrimitiveAttribute(primitiveB, primitiveTagHandle, 200));
            Assert.AreEqual(CoreResult.Success, detail.TrySetPrimitiveAttribute(primitiveC, primitiveTagHandle, 300));

            Assert.IsTrue(detail.RemoveVertex(v1, VertexDeletePolicy.DeleteIncidentPrimitives));

            var compiled = detail.Compile(Allocator.Temp);
            try
            {
                Assert.AreEqual(4, compiled.VertexCount);
                Assert.AreEqual(1, compiled.PrimitiveCount);

                Assert.AreEqual(CoreResult.Success,
                    compiled.TryGetAttributeAccessor<float>(MeshDomain.Vertex, VertexWeightAttribute, out var vertexWeights));
                Assert.AreEqual(10f, vertexWeights[0]);
                Assert.AreEqual(12f, vertexWeights[1]);
                Assert.AreEqual(13f, vertexWeights[2]);
                Assert.AreEqual(14f, vertexWeights[3]);

                Assert.AreEqual(CoreResult.Success,
                    compiled.TryGetAttributeAccessor<int>(MeshDomain.Primitive, PrimitiveTagAttribute, out var primitiveTags));
                Assert.AreEqual(200, primitiveTags[0]);
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
}
