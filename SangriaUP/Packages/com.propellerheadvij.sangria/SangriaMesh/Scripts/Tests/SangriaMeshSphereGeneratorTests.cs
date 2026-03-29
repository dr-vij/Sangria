using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using SangriaMesh;

[TestFixture]
public class SangriaMeshSphereGeneratorTests
{
    [Test]
    [TestCase(3, 3)]
    [TestCase(4, 4)]
    [TestCase(8, 6)]
    [TestCase(12, 8)]
    [TestCase(32, 16)]
    public void TopologyCounts_MatchFormula(int lon, int lat)
    {
        SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(lon, lat,
            out int pointCount, out int vertexCount, out int primitiveCount);

        int interiorRingCount = lat - 1;
        Assert.AreEqual(2 + interiorRingCount * lon, pointCount);
        Assert.AreEqual(lon * lat, primitiveCount);
        Assert.AreEqual(lon * (2 + 4 * interiorRingCount), vertexCount);
    }

    [Test]
    [TestCase(3, 3)]
    [TestCase(4, 4)]
    [TestCase(8, 6)]
    [TestCase(12, 8)]
    public void EveryVertexBelongsToExactlyOnePrimitive(int lon, int lat)
    {
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(lon, lat,
                out _, out int vertexCount, out _);

            var vertexUseCounts = new int[vertexCount];
            var validPrimitives = new NativeList<int>(detail.PrimitiveCapacity, Allocator.Temp);
            try
            {
                detail.GetAllValidPrimitives(validPrimitives);

                for (int i = 0; i < validPrimitives.Length; i++)
                {
                    int primIdx = validPrimitives[i];
                    NativeSlice<int> vertices = detail.GetPrimitiveVertices(primIdx);
                    for (int vi = 0; vi < vertices.Length; vi++)
                    {
                        int vertexIndex = vertices[vi];
                        Assert.IsTrue(vertexIndex >= 0 && vertexIndex < vertexCount,
                            $"Vertex index {vertexIndex} out of range [0, {vertexCount})");
                        vertexUseCounts[vertexIndex]++;
                    }
                }
            }
            finally
            {
                validPrimitives.Dispose();
            }

            for (int v = 0; v < vertexCount; v++)
            {
                Assert.AreEqual(1, vertexUseCounts[v],
                    $"Vertex {v} is used by {vertexUseCounts[v]} primitives, expected exactly 1.");
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    [TestCase(3, 3)]
    [TestCase(4, 4)]
    [TestCase(8, 6)]
    [TestCase(12, 8)]
    public void AllVerticesMapToValidPoints(int lon, int lat)
    {
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(lon, lat,
                out int pointCount, out int vertexCount, out _);

            for (int v = 0; v < vertexCount; v++)
            {
                if (!detail.IsVertexAlive(v)) continue;
                int pointIndex = detail.GetVertexPoint(v);
                Assert.IsTrue(pointIndex >= 0 && pointIndex < pointCount,
                    $"Vertex {v} maps to point {pointIndex}, expected [0, {pointCount})");
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    [TestCase(3, 3)]
    [TestCase(8, 6)]
    [TestCase(12, 8)]
    public void PointsAreSharedBetweenPrimitives(int lon, int lat)
    {
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(lon, lat,
                out int pointCount, out int vertexCount, out _);

            var pointUseCounts = new int[pointCount];
            for (int v = 0; v < vertexCount; v++)
            {
                if (!detail.IsVertexAlive(v)) continue;
                pointUseCounts[detail.GetVertexPoint(v)]++;
            }

            int northPole = 0;
            int southPole = 1;
            Assert.AreEqual(lon, pointUseCounts[northPole],
                $"North pole point should be referenced by {lon} vertices (one per top cap triangle).");
            Assert.AreEqual(lon, pointUseCounts[southPole],
                $"South pole point should be referenced by {lon} vertices (one per bottom cap triangle).");

            for (int p = 2; p < pointCount; p++)
            {
                Assert.Greater(pointUseCounts[p], 0,
                    $"Interior point {p} should be referenced by at least one vertex.");
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    [TestCase(8, 6)]
    [TestCase(12, 8)]
    public void AllPointPositionsAreOnSphere(int lon, int lat)
    {
        const float radius = 0.5f;
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(radius, lon, lat, Allocator.TempJob);
        try
        {
            SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(lon, lat,
                out int pointCount, out _, out _);

            Assert.AreEqual(CoreResult.Success,
                detail.TryGetPointAccessor<float3>(AttributeID.Position, out var posAccessor));

            for (int p = 0; p < pointCount; p++)
            {
                float3 pos = posAccessor[p];
                float dist = math.length(pos);
                Assert.AreEqual(radius, dist, 1e-5f,
                    $"Point {p} at {pos} has distance {dist} from origin, expected {radius}.");
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    [TestCase(8, 6)]
    [TestCase(12, 8)]
    public void PrimitiveColorDoesNotLeakToNeighbors(int lon, int lat)
    {
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            detail.AddVertexAttribute<float4>(AttributeID.Color);
            Assert.AreEqual(CoreResult.Success,
                detail.TryGetVertexAccessor<float4>(AttributeID.Color, out var colorAccessor));

            float4 white = new float4(1f, 1f, 1f, 1f);
            for (int v = 0; v < detail.VertexCapacity; v++)
                colorAccessor[v] = white;

            var validPrimitives = new NativeList<int>(detail.PrimitiveCapacity, Allocator.Temp);
            try
            {
                detail.GetAllValidPrimitives(validPrimitives);
                Assert.Greater(validPrimitives.Length, 0);

                int targetPrimitive = validPrimitives[0];
                float4 red = new float4(1f, 0f, 0f, 1f);
                NativeSlice<int> targetVerts = detail.GetPrimitiveVertices(targetPrimitive);
                for (int vi = 0; vi < targetVerts.Length; vi++)
                    colorAccessor[targetVerts[vi]] = red;

                for (int i = 1; i < validPrimitives.Length; i++)
                {
                    int primIdx = validPrimitives[i];
                    NativeSlice<int> verts = detail.GetPrimitiveVertices(primIdx);
                    for (int vi = 0; vi < verts.Length; vi++)
                    {
                        float4 c = colorAccessor[verts[vi]];
                        Assert.AreEqual(white, c,
                            $"Vertex {verts[vi]} of primitive {primIdx} has color {c}, " +
                            $"expected white. Color leaked from primitive {targetPrimitive}.");
                    }
                }
            }
            finally
            {
                validPrimitives.Dispose();
            }
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    [TestCase(8, 6)]
    [TestCase(12, 8)]
    public void UVSeamHasCorrectValues(int lon, int lat)
    {
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            Assert.AreEqual(CoreResult.Success,
                detail.TryGetVertexAccessor<float2>(AttributeID.UV0, out var uvAccessor));

            SangriaMeshSphereGenerator.GetUvSphereTopologyCounts(lon, lat,
                out _, out int vertexCount, out _);

            bool foundUvZero = false;
            bool foundUvOne = false;

            for (int v = 0; v < vertexCount; v++)
            {
                if (!detail.IsVertexAlive(v)) continue;
                float2 uv = uvAccessor[v];

                Assert.IsTrue(uv.x >= 0f && uv.x <= 1f,
                    $"Vertex {v} UV.x = {uv.x} out of [0, 1] range.");
                Assert.IsTrue(uv.y >= 0f && uv.y <= 1f,
                    $"Vertex {v} UV.y = {uv.y} out of [0, 1] range.");

                if (math.abs(uv.x) < 1e-5f) foundUvZero = true;
                if (math.abs(uv.x - 1f) < 1e-5f) foundUvOne = true;
            }

            Assert.IsTrue(foundUvZero, "Expected at least one vertex with UV.x ≈ 0.");
            Assert.IsTrue(foundUvOne, "Expected at least one vertex with UV.x ≈ 1 (UV seam).");
        }
        finally
        {
            detail.Dispose();
        }
    }

    [Test]
    public void CompiledSphereHasCorrectCounts()
    {
        const int lon = 8;
        const int lat = 6;
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            var compiled = detail.Compile(Allocator.TempJob);
            try
            {
                int interiorRingCount = lat - 1;
                Assert.AreEqual(2 + interiorRingCount * lon, compiled.PointCount);
                Assert.AreEqual(lon * (2 + 4 * interiorRingCount), compiled.VertexCount);
                Assert.AreEqual(lon * lat, compiled.PrimitiveCount);
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
    [TestCase(3, 3)]
    [TestCase(8, 6)]
    public void PrimitiveSizesAreCorrect(int lon, int lat)
    {
        var detail = SangriaMeshSphereGenerator.CreateUvSphere(0.5f, lon, lat, Allocator.TempJob);
        try
        {
            var validPrimitives = new NativeList<int>(detail.PrimitiveCapacity, Allocator.Temp);
            try
            {
                detail.GetAllValidPrimitives(validPrimitives);

                int triangleCount = 0;
                int quadCount = 0;

                for (int i = 0; i < validPrimitives.Length; i++)
                {
                    int primIdx = validPrimitives[i];
                    int vertCount = detail.GetPrimitiveVertices(primIdx).Length;
                    if (vertCount == 3) triangleCount++;
                    else if (vertCount == 4) quadCount++;
                    else Assert.Fail($"Primitive {primIdx} has {vertCount} vertices, expected 3 or 4.");
                }

                int expectedTriangles = lon * 2;
                int expectedQuads = (lat - 2) * lon;
                Assert.AreEqual(expectedTriangles, triangleCount,
                    $"Expected {expectedTriangles} triangles (top + bottom caps).");
                Assert.AreEqual(expectedQuads, quadCount,
                    $"Expected {expectedQuads} quads (middle bands).");
            }
            finally
            {
                validPrimitives.Dispose();
            }
        }
        finally
        {
            detail.Dispose();
        }
    }
}
