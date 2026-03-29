using System;
using System.Reflection;
using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class NativeDetailTriangulatorTests
{
    // ──────────────────────────────────────────────────────────────
    //  Box: full triangulation + all attribute domains + topology
    // ──────────────────────────────────────────────────────────────

    [Test]
    public void Box_Fan_TriangulatesWithAllAttributes()
    {
        AssertBoxTriangulation(TriangulationMode.Fan);
    }

    [Test]
    public void Box_EarClipping_TriangulatesWithAllAttributes()
    {
        AssertBoxTriangulation(TriangulationMode.EarClipping);
    }

    [Test]
    public void Box_Tess_TriangulatesWithAllAttributes()
    {
        AssertBoxTriangulation(TriangulationMode.Tess);
    }

    // ──────────────────────────────────────────────────────────────
    //  Sphere: triangulation correctness across modes
    // ──────────────────────────────────────────────────────────────

    [Test]
    public void Sphere_Fan_TriangulatesCorrectly()
    {
        AssertSphereTriangulation(TriangulationMode.Fan);
    }

    [Test]
    public void Sphere_EarClipping_TriangulatesCorrectly()
    {
        AssertSphereTriangulation(TriangulationMode.EarClipping);
    }

    [Test]
    public void Sphere_Tess_TriangulatesCorrectly()
    {
        AssertSphereTriangulation(TriangulationMode.Tess);
    }

    // ──────────────────────────────────────────────────────────────
    //  TriangulateInPlace
    // ──────────────────────────────────────────────────────────────

    [Test]
    public void Box_TriangulateInPlace_ProducesValidTriangles()
    {
        var detail = SangriaMeshBoxGenerator.CreateBox(new float3(1f, 2f, 3f), Allocator.TempJob);
        try
        {
            CoreResult result = NativeDetailTriangulator.TriangulateInPlace(
                ref detail, TriangulationMode.Fan, Allocator.TempJob);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(12, detail.PrimitiveCount);
            AssertAllTriangles(ref detail);
        }
        finally
        {
            detail.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Edge cases
    // ──────────────────────────────────────────────────────────────

    [Test]
    public void EmptyDetail_ReturnsSuccess()
    {
        var source = new NativeDetail(4, Allocator.TempJob);
        var output = new NativeDetail(4, Allocator.TempJob);
        try
        {
            CoreResult result = NativeDetailTriangulator.Triangulate(
                ref source, ref output, TriangulationMode.Fan);
            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(0, output.PrimitiveCount);
        }
        finally
        {
            source.Dispose();
            output.Dispose();
        }
    }

    [Test]
    public void NonEmptyOutput_ReturnsInvalidOperation()
    {
        var source = SangriaMeshBoxGenerator.CreateBox(new float3(1f, 1f, 1f), Allocator.TempJob);
        var output = SangriaMeshBoxGenerator.CreateBox(new float3(1f, 1f, 1f), Allocator.TempJob);
        try
        {
            CoreResult result = NativeDetailTriangulator.Triangulate(
                ref source, ref output, TriangulationMode.Fan);
            Assert.AreEqual(CoreResult.InvalidOperation, result);
        }
        finally
        {
            source.Dispose();
            output.Dispose();
        }
    }

    [Test]
    public void PrimitiveWithLessThan3Vertices_ReturnsInvalidOperation()
    {
        var source = new NativeDetail(4, Allocator.TempJob);
        var output = new NativeDetail(4, Allocator.TempJob);
        try
        {
            InjectMalformedPrimitiveWithLessThan3Vertices(ref source);
            Assert.Less(source.GetPrimitiveVertexCount(0), 3,
                "Test setup must create a malformed primitive with <3 vertices");

            CoreResult result = NativeDetailTriangulator.Triangulate(
                ref source, ref output, TriangulationMode.Fan);
            Assert.AreEqual(CoreResult.InvalidOperation, result);
        }
        finally
        {
            source.Dispose();
            output.Dispose();
        }
    }

    [Test]
    public void AlreadyTriangulated_PassesThrough()
    {
        var source = new NativeDetail(8, Allocator.TempJob);
        var output = new NativeDetail(8, Allocator.TempJob);
        try
        {
            int p0 = source.AddPoint(new float3(0, 0, 0));
            int p1 = source.AddPoint(new float3(1, 0, 0));
            int p2 = source.AddPoint(new float3(0, 1, 0));
            int v0 = source.AddVertex(p0);
            int v1 = source.AddVertex(p1);
            int v2 = source.AddVertex(p2);
            var triVerts = new NativeArray<int>(new[] { v0, v1, v2 }, Allocator.Temp);
            source.AddPrimitive(triVerts);
            triVerts.Dispose();

            CoreResult result = NativeDetailTriangulator.Triangulate(
                ref source, ref output, TriangulationMode.EarClipping);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(1, output.PrimitiveCount);
            Assert.AreEqual(3, output.PointCount);
        }
        finally
        {
            source.Dispose();
            output.Dispose();
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────

    private static void AssertBoxTriangulation(TriangulationMode mode)
    {
        var source = SangriaMeshBoxGenerator.CreateBox(new float3(2f, 3f, 4f), Allocator.TempJob);
        var output = new NativeDetail(source.PointCount, Allocator.TempJob);
        try
        {
            CoreResult result = NativeDetailTriangulator.Triangulate(
                ref source, ref output, mode);

            Assert.AreEqual(CoreResult.Success, result, "Triangulation must succeed");
            Assert.AreEqual(12, output.PrimitiveCount, "6 quads -> 12 triangles");
            AssertAllTriangles(ref output);

            // Corner semantics: vertex/point counts must be preserved
            Assert.AreEqual(source.PointCount, output.PointCount, "Point count must match source");
            Assert.AreEqual(source.VertexCount, output.VertexCount, "Vertex count must match source (corner semantics)");

            // All source positions must survive
            AssertPositionsPreserved(ref source, ref output);

            // Vertex-domain attributes must be transferred
            Assert.IsTrue(output.HasVertexAttribute(AttributeID.Normal), "Vertex Normal must be transferred");
            Assert.IsTrue(output.HasVertexAttribute(AttributeID.UV0), "Vertex UV0 must be transferred");

            // Compiled output must be triangle-only with valid attribute data
            var compiled = output.Compile(Allocator.TempJob);
            try
            {
                Assert.IsTrue(compiled.IsTriangleOnlyTopology, "Compiled output must be triangle-only");
                Assert.AreEqual(12 * 3, compiled.PrimitiveVerticesDense.Length, "36 vertex indices expected");

                // Normals must be unit-length
                Assert.AreEqual(CoreResult.Success,
                    compiled.TryGetAttributeAccessor<float3>(MeshDomain.Vertex, AttributeID.Normal, out var normals));
                for (int i = 0; i < normals.Length; i++)
                {
                    float len = math.length(normals[i]);
                    Assert.IsTrue(len > 0.99f && len < 1.01f,
                        $"Normal {i} = {normals[i]}, length {len} is not unit");
                }

                // UVs must be in [0,1]
                Assert.AreEqual(CoreResult.Success,
                    compiled.TryGetAttributeAccessor<float2>(MeshDomain.Vertex, AttributeID.UV0, out var uvs));
                for (int i = 0; i < uvs.Length; i++)
                {
                    float2 uv = uvs[i];
                    Assert.IsTrue(uv.x >= -0.001f && uv.x <= 1.001f && uv.y >= -0.001f && uv.y <= 1.001f,
                        $"UV {i} = {uv} outside [0,1]");
                }

                // Positions must be within box half-extents
                Assert.AreEqual(CoreResult.Success,
                    compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var positions));
                float3 halfSize = new float3(1f, 1.5f, 2f);
                for (int i = 0; i < positions.Length; i++)
                {
                    Assert.IsTrue(math.all(positions[i] >= -halfSize - 0.001f) && math.all(positions[i] <= halfSize + 0.001f),
                        $"Position {positions[i]} outside box bounds");
                }
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            source.Dispose();
            output.Dispose();
        }
    }

    private static void AssertSphereTriangulation(TriangulationMode mode)
    {
        const int lon = 8, lat = 6;
        var source = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        var output = new NativeDetail(source.PointCount, Allocator.TempJob);
        try
        {
            CoreResult result = NativeDetailTriangulator.Triangulate(
                ref source, ref output, mode);

            Assert.AreEqual(CoreResult.Success, result);
            AssertAllTriangles(ref output);

            int expectedTriangles = lon * 2 + lon * (lat - 2) * 2;
            Assert.AreEqual(expectedTriangles, output.PrimitiveCount,
                "Sphere triangle count mismatch");

            var compiled = output.Compile(Allocator.TempJob);
            try
            {
                Assert.IsTrue(compiled.IsTriangleOnlyTopology, "Compiled sphere must be triangle-only");
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            source.Dispose();
            output.Dispose();
        }
    }

    private static void AssertPositionsPreserved(ref NativeDetail source, ref NativeDetail output)
    {
        var srcPts = new NativeList<int>(source.PointCount, Allocator.TempJob);
        var outPts = new NativeList<int>(output.PointCount, Allocator.TempJob);
        var sourcePositions = new NativeHashSet<float3>(source.PointCount, Allocator.TempJob);
        var outputPositions = new NativeHashSet<float3>(output.PointCount, Allocator.TempJob);
        try
        {
            source.GetAllValidPoints(srcPts);
            output.GetAllValidPoints(outPts);

            for (int i = 0; i < srcPts.Length; i++)
                sourcePositions.Add(source.GetPointPosition(srcPts[i]));
            for (int i = 0; i < outPts.Length; i++)
                outputPositions.Add(output.GetPointPosition(outPts[i]));

            for (int i = 0; i < outPts.Length; i++)
                Assert.IsTrue(sourcePositions.Contains(output.GetPointPosition(outPts[i])),
                    $"Output position not in source");
            for (int i = 0; i < srcPts.Length; i++)
                Assert.IsTrue(outputPositions.Contains(source.GetPointPosition(srcPts[i])),
                    $"Source position missing in output");

            Assert.AreEqual(sourcePositions.Count, outputPositions.Count,
                "Unique position count must match");
        }
        finally
        {
            srcPts.Dispose();
            outPts.Dispose();
            sourcePositions.Dispose();
            outputPositions.Dispose();
        }
    }

    private static void AssertAllTriangles(ref NativeDetail detail)
    {
        var prims = new NativeList<int>(detail.PrimitiveCount, Allocator.TempJob);
        detail.GetAllValidPrimitives(prims);

        for (int i = 0; i < prims.Length; i++)
        {
            int vertCount = detail.GetPrimitiveVertexCount(prims[i]);
            Assert.AreEqual(3, vertCount,
                $"Primitive {prims[i]} has {vertCount} vertices, expected 3");
        }

        prims.Dispose();
    }

    private static void InjectMalformedPrimitiveWithLessThan3Vertices(ref NativeDetail detail)
    {
        MethodInfo allocateDenseTopology = typeof(NativeDetail).GetMethod(
            "AllocateDenseTopologyUnchecked",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (allocateDenseTopology == null)
            throw new InvalidOperationException("Failed to find AllocateDenseTopologyUnchecked via reflection.");

        object boxedDetail = detail;
        allocateDenseTopology.Invoke(boxedDetail, new object[] { 0, 0, 1, false, true });
        detail = (NativeDetail)boxedDetail;
    }
}
