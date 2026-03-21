using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using SangriaMesh;

public class SangriaMeshCoreTests
{
    private const int PointTempAttribute = 2001;
    private const int PrimitiveMaterialAttribute = 3001;
    private const int ResourceId = 4001;
    private const int LatePointAttribute = 5001;
    private const int HandleSafetyAttrA = 6001;
    private const int HandleSafetyAttrB = 6002;
    private const int VersionPointAttribute = 7001;
    private const int VersionVertexAttribute = 7002;
    private const int VersionPrimitiveAttribute = 7003;
    private const int VersionResourceId = 7004;
    private const int DenseClearPointAttribute = 7101;
    private const int DenseClearVertexAttribute = 7102;
    private const int DenseClearPrimitiveAttribute = 7103;
    private const int GrowthPointAttributeBase = 7200;
    private const int GrowthResourceBase = 7300;

    private struct TestResource
    {
        public float Value;
        public int Flags;
    }

    [Test]
    public void CompileDenseTopologyAndAttributes()
    {
        var detail = new NativeDetail(8, Allocator.TempJob);

        Assert.AreEqual(CoreResult.Success, detail.AddPointAttribute<float>(PointTempAttribute));
        Assert.AreEqual(CoreResult.Success, detail.AddPrimitiveAttribute<int>(PrimitiveMaterialAttribute));

        int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
        int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
        int p2 = detail.AddPoint(new float3(0f, 1f, 0f));

        int v0 = detail.AddVertex(p0);
        int v1 = detail.AddVertex(p1);
        int v2 = detail.AddVertex(p2);

        using var triangle = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
        int prim = detail.AddPrimitive(triangle);

        Assert.GreaterOrEqual(prim, 0);

        Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttributeHandle<float>(PointTempAttribute, out var tempHandle));
        Assert.AreEqual(CoreResult.Success, detail.TrySetPointAttribute(p0, tempHandle, 10f));
        Assert.AreEqual(CoreResult.Success, detail.TrySetPointAttribute(p1, tempHandle, 20f));
        Assert.AreEqual(CoreResult.Success, detail.TrySetPointAttribute(p2, tempHandle, 30f));

        Assert.AreEqual(CoreResult.Success, detail.TryGetPrimitiveAttributeHandle<int>(PrimitiveMaterialAttribute, out var materialHandle));
        Assert.AreEqual(CoreResult.Success, detail.TrySetPrimitiveAttribute(prim, materialHandle, 77));

        var resource = new TestResource { Value = 123.5f, Flags = 9 };
        Assert.AreEqual(CoreResult.Success, detail.SetResource(ResourceId, resource));

        var compiled = detail.Compile(Allocator.TempJob);

        Assert.AreEqual(3, compiled.PointCount);
        Assert.AreEqual(3, compiled.VertexCount);
        Assert.AreEqual(1, compiled.PrimitiveCount);

        Assert.AreEqual(3, compiled.VertexToPointDense.Length);
        Assert.AreEqual(2, compiled.PrimitiveOffsetsDense.Length);
        Assert.AreEqual(3, compiled.PrimitiveVerticesDense.Length);

        Assert.AreEqual(0, compiled.PrimitiveOffsetsDense[0]);
        Assert.AreEqual(3, compiled.PrimitiveOffsetsDense[1]);

        Assert.AreEqual(CoreResult.Success,
            compiled.TryGetAttributeAccessor<float>(MeshDomain.Point, PointTempAttribute, out var pointTemps));
        Assert.AreEqual(10f, pointTemps[0]);
        Assert.AreEqual(20f, pointTemps[1]);
        Assert.AreEqual(30f, pointTemps[2]);

        Assert.AreEqual(CoreResult.Success,
            compiled.TryGetAttributeAccessor<int>(MeshDomain.Primitive, PrimitiveMaterialAttribute, out var primitiveMaterials));
        Assert.AreEqual(77, primitiveMaterials[0]);

        Assert.AreEqual(CoreResult.Success, compiled.TryGetResource<TestResource>(ResourceId, out var compiledResource));
        Assert.AreEqual(123.5f, compiledResource.Value);
        Assert.AreEqual(9, compiledResource.Flags);

        compiled.Dispose();
        detail.Dispose();
    }

    [Test]
    public void HandleInvalidationWorks()
    {
        var detail = new NativeDetail(2, Allocator.TempJob);

        int p0 = detail.AddPoint(new float3(0f, 0f, 0f), out var oldHandle);
        Assert.IsTrue(detail.IsPointHandleValid(oldHandle));
        Assert.IsTrue(detail.RemovePoint(p0));
        Assert.IsFalse(detail.IsPointHandleValid(oldHandle));

        int p1 = detail.AddPoint(new float3(1f, 0f, 0f), out var newHandle);
        Assert.AreEqual(p0, p1);
        Assert.AreNotEqual(oldHandle.Generation, newHandle.Generation);
        Assert.IsFalse(detail.IsPointHandleValid(oldHandle));
        Assert.IsTrue(detail.IsPointHandleValid(newHandle));

        detail.Dispose();
    }

    [Test]
    public void StaleAttributeHandleIsRejectedAfterRemoveAndReAdd()
    {
        var detail = new NativeDetail(4, Allocator.TempJob);
        try
        {
            int pointIndex = detail.AddPoint(new float3(0f, 0f, 0f));

            Assert.AreEqual(CoreResult.Success, detail.AddPointAttribute<float>(HandleSafetyAttrA));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttributeHandle<float>(HandleSafetyAttrA, out var staleHandle));
            Assert.AreEqual(CoreResult.Success, detail.RemovePointAttribute(HandleSafetyAttrA));

            Assert.AreEqual(CoreResult.Success, detail.AddPointAttribute<float>(HandleSafetyAttrB));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttributeHandle<float>(HandleSafetyAttrB, out var activeHandle));

            Assert.AreEqual(CoreResult.InvalidHandle, detail.TrySetPointAttribute(pointIndex, staleHandle, 10f));
            Assert.AreEqual(CoreResult.InvalidHandle, detail.TryGetPointAttribute(pointIndex, staleHandle, out _));

            Assert.AreEqual(CoreResult.Success, detail.TrySetPointAttribute(pointIndex, activeHandle, 20f));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttribute(pointIndex, activeHandle, out float value));
            Assert.AreEqual(20f, value);
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void PointHandleStaysInvalidAfterDenseRebuildPath()
    {
        var detail = new NativeDetail(8, Allocator.TempJob);
        try
        {
            detail.AddPoint(new float3(1f, 2f, 3f), out var oldHandle);
            Assert.IsTrue(detail.IsPointHandleValid(oldHandle));

            SangriaMeshSphereGenerator.PopulateUvSphere(ref detail, 0.5f, 8, 6);

            Assert.IsFalse(detail.IsPointHandleValid(oldHandle));
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void AttributeVersionIncrementsForSchemaAndResourceMutations()
    {
        var detail = new NativeDetail(8, Allocator.TempJob);
        try
        {
            uint expectedVersion = detail.AttributeVersion;

            Assert.AreEqual(CoreResult.Success, detail.AddPointAttribute<float>(VersionPointAttribute));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.Success, detail.AddVertexAttribute<int>(VersionVertexAttribute));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.Success, detail.AddPrimitiveAttribute<float>(VersionPrimitiveAttribute));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.AlreadyExists, detail.AddPointAttribute<float>(VersionPointAttribute));
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.Success, detail.RemovePointAttribute(VersionPointAttribute));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.Success, detail.RemoveVertexAttribute(VersionVertexAttribute));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.Success, detail.RemovePrimitiveAttribute(VersionPrimitiveAttribute));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            var resource = new TestResource { Value = 1.5f, Flags = 11 };
            Assert.AreEqual(CoreResult.Success, detail.SetResource(VersionResourceId, resource));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            resource.Value = 2.5f;
            resource.Flags = 12;
            Assert.AreEqual(CoreResult.Success, detail.SetResource(VersionResourceId, resource));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.NotFound, detail.RemoveResource(VersionResourceId + 1));
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);

            Assert.AreEqual(CoreResult.Success, detail.RemoveResource(VersionResourceId));
            expectedVersion++;
            Assert.AreEqual(expectedVersion, detail.AttributeVersion);
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void DenseRebuildClearsAllCustomAttributeDomains()
    {
        var detail = new NativeDetail(16, Allocator.TempJob);
        try
        {
            Assert.AreEqual(CoreResult.Success, detail.AddPointAttribute<float>(DenseClearPointAttribute));
            Assert.AreEqual(CoreResult.Success, detail.AddVertexAttribute<int>(DenseClearVertexAttribute));
            Assert.AreEqual(CoreResult.Success, detail.AddPrimitiveAttribute<float>(DenseClearPrimitiveAttribute));

            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));
            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);

            using (var tri = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp))
                Assert.GreaterOrEqual(detail.AddPrimitive(tri), 0);

            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttributeHandle<float>(DenseClearPointAttribute, out var pointHandle));
            Assert.AreEqual(CoreResult.Success, detail.TryGetVertexAttributeHandle<int>(DenseClearVertexAttribute, out var vertexHandle));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPrimitiveAttributeHandle<float>(DenseClearPrimitiveAttribute, out var primitiveHandle));

            Assert.AreEqual(CoreResult.Success, detail.TrySetPointAttribute(p0, pointHandle, 123f));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v0, vertexHandle, 456));
            Assert.AreEqual(CoreResult.Success, detail.TrySetPrimitiveAttribute(0, primitiveHandle, 789f));

            SangriaMeshSphereGenerator.PopulateUvSphere(ref detail, 0.5f, 8, 6);

            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttribute(0, pointHandle, out float pointValue));
            Assert.AreEqual(CoreResult.Success, detail.TryGetVertexAttribute(0, vertexHandle, out int vertexValue));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPrimitiveAttribute(0, primitiveHandle, out float primitiveValue));

            Assert.AreEqual(0f, pointValue);
            Assert.AreEqual(0, vertexValue);
            Assert.AreEqual(0f, primitiveValue);
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RegisteringManyPointAttributesGrowsInternalMapCapacity()
    {
        var detail = new NativeDetail(4, Allocator.TempJob);
        try
        {
            const int attributeCount = 96;
            for (int i = 0; i < attributeCount; i++)
            {
                int attributeId = GrowthPointAttributeBase + i;
                Assert.AreEqual(CoreResult.Success, detail.AddPointAttribute<float>(attributeId));
                Assert.IsTrue(detail.HasPointAttribute(attributeId));
            }

            int pointIndex = detail.AddPoint(new float3(1f, 2f, 3f));
            int sampleAttributeId = GrowthPointAttributeBase + attributeCount - 1;
            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttributeHandle<float>(sampleAttributeId, out var handle));
            Assert.AreEqual(CoreResult.Success, detail.TrySetPointAttribute(pointIndex, handle, 42f));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttribute(pointIndex, handle, out float value));
            Assert.AreEqual(42f, value);
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void RegisteringManyResourcesGrowsInternalMapCapacity()
    {
        var detail = new NativeDetail(2, Allocator.TempJob);
        try
        {
            const int resourceCount = 128;
            for (int i = 0; i < resourceCount; i++)
                Assert.AreEqual(CoreResult.Success, detail.SetResource(GrowthResourceBase + i, i * 3));

            for (int i = 0; i < resourceCount; i++)
            {
                Assert.AreEqual(CoreResult.Success, detail.TryGetResource<int>(GrowthResourceBase + i, out int value));
                Assert.AreEqual(i * 3, value);
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void LateRegisteredPointAttributeIsZeroInitialized()
    {
        var detail = new NativeDetail(4, Allocator.TempJob);
        try
        {
            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));

            Assert.AreEqual(CoreResult.Success, detail.AddPointAttribute<float>(LatePointAttribute));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttributeHandle<float>(LatePointAttribute, out var handle));

            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttribute(p0, handle, out float v0));
            Assert.AreEqual(CoreResult.Success, detail.TryGetPointAttribute(p1, handle, out float v1));

            Assert.AreEqual(0f, v0);
            Assert.AreEqual(0f, v1);
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void ConvertsTriangleToUnityMesh()
    {
        var detail = new NativeDetail(8, Allocator.TempJob);
        try
        {
            Assert.AreEqual(CoreResult.Success, detail.AddVertexAttribute<float3>(AttributeID.Normal));
            Assert.AreEqual(CoreResult.Success, detail.AddVertexAttribute<float2>(AttributeID.UV0));

            Assert.AreEqual(CoreResult.Success, detail.TryGetVertexAttributeHandle<float3>(AttributeID.Normal, out var normalHandle));
            Assert.AreEqual(CoreResult.Success, detail.TryGetVertexAttributeHandle<float2>(AttributeID.UV0, out var uvHandle));

            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(0f, 1f, 0f));

            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);

            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v0, normalHandle, new float3(0f, 0f, 1f)));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v1, normalHandle, new float3(0f, 0f, 1f)));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v2, normalHandle, new float3(0f, 0f, 1f)));

            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v0, uvHandle, new float2(0f, 0f)));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v1, uvHandle, new float2(1f, 0f)));
            Assert.AreEqual(CoreResult.Success, detail.TrySetVertexAttribute(v2, uvHandle, new float2(0f, 1f)));

            using var triangle = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
            Assert.GreaterOrEqual(detail.AddPrimitive(triangle), 0);

            var mesh = detail.ToUnityMesh("TestMesh", Allocator.TempJob);
            try
            {
                Assert.AreEqual("TestMesh", mesh.name);
                Assert.AreEqual(3, mesh.vertexCount);
                Assert.AreEqual(3, mesh.triangles.Length);
                Assert.AreEqual(3, mesh.normals.Length);
                Assert.AreEqual(3, mesh.uv.Length);

                Assert.AreEqual(new float3(0f, 0f, 0f), new float3(mesh.vertices[0].x, mesh.vertices[0].y, mesh.vertices[0].z));
                Assert.AreEqual(new float3(1f, 0f, 0f), new float3(mesh.vertices[1].x, mesh.vertices[1].y, mesh.vertices[1].z));
                Assert.AreEqual(new float3(0f, 1f, 0f), new float3(mesh.vertices[2].x, mesh.vertices[2].y, mesh.vertices[2].z));

                Assert.AreEqual(0, mesh.triangles[0]);
                Assert.AreEqual(1, mesh.triangles[1]);
                Assert.AreEqual(2, mesh.triangles[2]);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void QuadPrimitiveIsTriangulatedOnUnityMeshConversion()
    {
        var detail = new NativeDetail(8, Allocator.TempJob);
        try
        {
            float2 p0v = new float2(0f, 0f);
            float2 p1v = new float2(1f, 0f);
            float2 p2v = new float2(1f, 1f);
            float2 p3v = new float2(0f, 1f);

            int p0 = detail.AddPoint(new float3(p0v.x, p0v.y, 0f));
            int p1 = detail.AddPoint(new float3(p1v.x, p1v.y, 0f));
            int p2 = detail.AddPoint(new float3(p2v.x, p2v.y, 0f));
            int p3 = detail.AddPoint(new float3(p3v.x, p3v.y, 0f));

            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);

            using var quad = new NativeArray<int>(new[] { v0, v1, v2, v3 }, Allocator.Temp);
            Assert.GreaterOrEqual(detail.AddPrimitive(quad), 0);

            var mesh = detail.ToUnityMesh("QuadMesh", Allocator.TempJob);
            try
            {
                Assert.AreEqual("QuadMesh", mesh.name);
                Assert.AreEqual(4, mesh.vertexCount);
                Assert.AreEqual(6, mesh.triangles.Length);

                var polygon = new[] { p0v, p1v, p2v, p3v };
                float polygonArea = PolygonArea2D(polygon);

                float trianglesArea = 0f;
                int[] triangles = mesh.triangles;
                var verts = mesh.vertices;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    trianglesArea += TriangleArea2D(
                        new float3(verts[triangles[i]].x, verts[triangles[i]].y, verts[triangles[i]].z),
                        new float3(verts[triangles[i + 1]].x, verts[triangles[i + 1]].y, verts[triangles[i + 1]].z),
                        new float3(verts[triangles[i + 2]].x, verts[triangles[i + 2]].y, verts[triangles[i + 2]].z));
                }

                Assert.AreEqual(polygonArea, trianglesArea, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void PentagonPrimitiveIsTriangulatedOnUnityMeshConversion()
    {
        var detail = new NativeDetail(16, Allocator.TempJob);
        try
        {
            float2 p0v = new float2(0f, 0f);
            float2 p1v = new float2(1f, 0f);
            float2 p2v = new float2(1.3f, 0.8f);
            float2 p3v = new float2(0.5f, 1.4f);
            float2 p4v = new float2(-0.2f, 0.8f);

            int p0 = detail.AddPoint(new float3(p0v.x, p0v.y, 0f));
            int p1 = detail.AddPoint(new float3(p1v.x, p1v.y, 0f));
            int p2 = detail.AddPoint(new float3(p2v.x, p2v.y, 0f));
            int p3 = detail.AddPoint(new float3(p3v.x, p3v.y, 0f));
            int p4 = detail.AddPoint(new float3(p4v.x, p4v.y, 0f));

            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);
            int v4 = detail.AddVertex(p4);

            using var pentagon = new NativeArray<int>(new[] { v0, v1, v2, v3, v4 }, Allocator.Temp);
            Assert.GreaterOrEqual(detail.AddPrimitive(pentagon), 0);

            var mesh = detail.ToUnityMesh("PentagonMesh", Allocator.TempJob);
            try
            {
                Assert.AreEqual("PentagonMesh", mesh.name);
                Assert.AreEqual(5, mesh.vertexCount);
                Assert.AreEqual(9, mesh.triangles.Length);

                var polygon = new[] { p0v, p1v, p2v, p3v, p4v };
                float polygonArea = PolygonArea2D(polygon);

                float trianglesArea = 0f;
                int[] triangles = mesh.triangles;
                var verts = mesh.vertices;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    trianglesArea += TriangleArea2D(
                        new float3(verts[triangles[i]].x, verts[triangles[i]].y, verts[triangles[i]].z),
                        new float3(verts[triangles[i + 1]].x, verts[triangles[i + 1]].y, verts[triangles[i + 1]].z),
                        new float3(verts[triangles[i + 2]].x, verts[triangles[i + 2]].y, verts[triangles[i + 2]].z));
                }

                Assert.AreEqual(polygonArea, trianglesArea, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void ConcavePolygonTriangulationPreservesPolygonArea()
    {
        var detail = new NativeDetail(16, Allocator.TempJob);
        try
        {
            // Concave pentagon in XY plane.
            float2 p0v = new float2(0f, 0f);
            float2 p1v = new float2(3f, 0f);
            float2 p2v = new float2(3f, 3f);
            float2 p3v = new float2(1.5f, 1f);
            float2 p4v = new float2(0f, 3f);

            int p0 = detail.AddPoint(new float3(p0v.x, p0v.y, 0f));
            int p1 = detail.AddPoint(new float3(p1v.x, p1v.y, 0f));
            int p2 = detail.AddPoint(new float3(p2v.x, p2v.y, 0f));
            int p3 = detail.AddPoint(new float3(p3v.x, p3v.y, 0f));
            int p4 = detail.AddPoint(new float3(p4v.x, p4v.y, 0f));

            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p1);
            int v2 = detail.AddVertex(p2);
            int v3 = detail.AddVertex(p3);
            int v4 = detail.AddVertex(p4);

            using var concave = new NativeArray<int>(new[] { v0, v1, v2, v3, v4 }, Allocator.Temp);
            Assert.GreaterOrEqual(detail.AddPrimitive(concave), 0);

            var mesh = detail.ToUnityMesh("ConcaveMesh", Allocator.TempJob);
            try
            {
                Assert.AreEqual(5, mesh.vertexCount);
                Assert.AreEqual(9, mesh.triangles.Length);

                var polygon = new[] { p0v, p1v, p2v, p3v, p4v };
                float polygonArea = PolygonArea2D(polygon);

                float trianglesArea = 0f;
                int[] triangles = mesh.triangles;
                var verts = mesh.vertices;
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    trianglesArea += TriangleArea2D(
                        new float3(verts[triangles[i]].x, verts[triangles[i]].y, verts[triangles[i]].z),
                        new float3(verts[triangles[i + 1]].x, verts[triangles[i + 1]].y, verts[triangles[i + 1]].z),
                        new float3(verts[triangles[i + 2]].x, verts[triangles[i + 2]].y, verts[triangles[i + 2]].z));
                }

                Assert.AreEqual(polygonArea, trianglesArea, 1e-4f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void SelfIntersectingPolygonFallsBackWithoutIndexOverflow()
    {
        var detail = new NativeDetail(16, Allocator.TempJob);
        try
        {
            float2[] polygon =
            {
                new float2(0.7239871f, 0.23342294f),
                new float2(2.5099974f, 1.9871782f),
                new float2(4.149402f, 0.15519628f),
                new float2(5.1459413f, 0.96049553f),
                new float2(5.767389f, 1.8099262f)
            };

            var primitiveVertices = new NativeArray<int>(polygon.Length, Allocator.Temp);
            try
            {
                for (int i = 0; i < polygon.Length; i++)
                {
                    int pointIndex = detail.AddPoint(new float3(polygon[i].x, polygon[i].y, 0f));
                    primitiveVertices[i] = detail.AddVertex(pointIndex);
                }

                Assert.GreaterOrEqual(detail.AddPrimitive(primitiveVertices), 0);

                var mesh = detail.ToUnityMesh("SelfIntersectingPolygon", Allocator.TempJob);
                try
                {
                    Assert.AreEqual((polygon.Length - 2) * 3, mesh.triangles.Length);
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
            finally
            {
                if (primitiveVertices.IsCreated)
                    primitiveVertices.Dispose();
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void CompileSparseTopologyAfterDeletionProducesDenseResult()
    {
        var detail = new NativeDetail(8, Allocator.TempJob);
        try
        {
            int p0 = detail.AddPoint(new float3(0f, 0f, 0f));
            int p1 = detail.AddPoint(new float3(1f, 0f, 0f));
            int p2 = detail.AddPoint(new float3(1f, 1f, 0f));
            int p3 = detail.AddPoint(new float3(0f, 1f, 0f));

            Assert.IsTrue(detail.RemovePoint(p1));

            int v0 = detail.AddVertex(p0);
            int v1 = detail.AddVertex(p2);
            int v2 = detail.AddVertex(p3);

            using var tri = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
            Assert.GreaterOrEqual(detail.AddPrimitive(tri), 0);

            var compiled = detail.Compile(Allocator.TempJob);
            try
            {
                Assert.AreEqual(3, compiled.PointCount);
                Assert.AreEqual(3, compiled.VertexCount);
                Assert.AreEqual(1, compiled.PrimitiveCount);

                Assert.AreEqual(0, compiled.VertexToPointDense[0]);
                Assert.AreEqual(1, compiled.VertexToPointDense[1]);
                Assert.AreEqual(2, compiled.VertexToPointDense[2]);
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
    public void DetailThrowsAfterDispose()
    {
        var detail = new NativeDetail(4, Allocator.TempJob);
        detail.Dispose();

        Assert.Throws<System.ObjectDisposedException>(() =>
        {
            _ = detail.PointCount;
        });
        Assert.Throws<System.ObjectDisposedException>(() =>
        {
            detail.AddPoint(new float3(0f, 0f, 0f));
        });
        Assert.Throws<System.ObjectDisposedException>(() =>
        {
            detail.TryGetResource<int>(GrowthResourceBase, out _);
        });
    }

    [Test]
    public void CompiledDetailThrowsAfterDispose()
    {
        var detail = new NativeDetail(4, Allocator.TempJob);
        try
        {
            int pointIndex = detail.AddPoint(new float3(0f, 0f, 0f));
            int vertexIndex = detail.AddVertex(pointIndex);
            using var tri = new NativeArray<int>(new[] { vertexIndex, vertexIndex, vertexIndex }, Allocator.Temp);
            Assert.GreaterOrEqual(detail.AddPrimitive(tri), 0);

            var compiled = detail.Compile(Allocator.TempJob);
            compiled.Dispose();

            Assert.IsTrue(compiled.IsDisposed);
            Assert.Throws<System.ObjectDisposedException>(() =>
            {
                compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out _);
            });
            Assert.Throws<System.ObjectDisposedException>(() =>
            {
                compiled.TryGetResource<int>(GrowthResourceBase, out _);
            });
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void SphereGeneratorCreatesExpectedCounts()
    {
        const int lon = 12;
        const int lat = 8;

        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            var compiled = detail.Compile(Allocator.TempJob);
            try
            {
                int expectedPoints = 2 + (lat - 1) * lon;
                int expectedPrimitives = 2 * lon * (lat - 1);
                int expectedVertices = 2 + (lat - 1) * (lon + 1);

                Assert.AreEqual(expectedPoints, compiled.PointCount);
                Assert.AreEqual(expectedVertices, compiled.VertexCount);
                Assert.AreEqual(expectedPrimitives, compiled.PrimitiveCount);
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
    public void BoxGeneratorCreatesExpectedCountsAndCornerSharing()
    {
        var detail = SangriaMeshBoxGenerator.CreateBox(new float3(2f, 3f, 4f), Allocator.TempJob);
        try
        {
            var compiled = detail.Compile(Allocator.TempJob);
            try
            {
                Assert.AreEqual(8, compiled.PointCount);
                Assert.AreEqual(24, compiled.VertexCount);
                Assert.AreEqual(12, compiled.PrimitiveCount);

                var pointUseCounts = new int[compiled.PointCount];
                for (int i = 0; i < compiled.VertexToPointDense.Length; i++)
                    pointUseCounts[compiled.VertexToPointDense[i]]++;

                for (int i = 0; i < pointUseCounts.Length; i++)
                    Assert.AreEqual(3, pointUseCounts[i], $"Point {i} should be referenced by exactly three face vertices.");

                Assert.AreEqual(
                    CoreResult.Success,
                    compiled.TryGetAttributeAccessor<float3>(MeshDomain.Vertex, AttributeID.Normal, out var vertexNormals));

                for (int face = 0; face < 6; face++)
                {
                    int faceStart = face * 4;
                    float3 faceNormal = vertexNormals[faceStart];

                    Assert.AreEqual(faceNormal, vertexNormals[faceStart + 1]);
                    Assert.AreEqual(faceNormal, vertexNormals[faceStart + 2]);
                    Assert.AreEqual(faceNormal, vertexNormals[faceStart + 3]);
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

    private static float PolygonArea2D(float2[] polygon)
    {
        float area2 = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            float2 a = polygon[i];
            float2 b = polygon[(i + 1) % polygon.Length];
            area2 += a.x * b.y - b.x * a.y;
        }

        return Mathf.Abs(area2) * 0.5f;
    }

    private static float TriangleArea2D(float3 a, float3 b, float3 c)
    {
        float2 ab = new float2(b.x - a.x, b.y - a.y);
        float2 ac = new float2(c.x - a.x, c.y - a.y);
        return math.abs(ab.x * ac.y - ab.y * ac.x) * 0.5f;
    }

}
