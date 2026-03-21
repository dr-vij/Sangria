using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class SangriaMeshTriangulationTests
{
    [Test]
    public void TriangulateContours_ConcavePolygon_ProducesTriangleOnlyDetail()
    {
        var positions = new NativeArray<float3>(5, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 5 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4 }, Allocator.Temp);
        var output = new NativeDetail(8, Allocator.TempJob);
        using var scratch = new TriangulationScratch();

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(3f, 0f, 0f);
            positions[2] = new float3(3f, 3f, 0f);
            positions[3] = new float3(1.5f, 1f, 0f);
            positions[4] = new float3(0f, 3f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateContours(in contours, ref output, scratch, TriangulationOptions.Default);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(5, output.PointCount);
            Assert.AreEqual(5, output.VertexCount);
            Assert.AreEqual(3, output.PrimitiveCount);

            var compiled = output.Compile(Allocator.TempJob);
            try
            {
                Assert.IsTrue(compiled.IsTriangleOnlyTopology);
                Assert.AreEqual(9, compiled.PrimitiveVerticesDense.Length);

                float expectedArea = PolygonArea2D(
                    new float2(0f, 0f),
                    new float2(3f, 0f),
                    new float2(3f, 3f),
                    new float2(1.5f, 1f),
                    new float2(0f, 3f));

                float actualArea = ComputeCompiledArea(in compiled);
                Assert.AreEqual(expectedArea, actualArea, 1e-4f);
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated)
                positions.Dispose();
            if (contourOffsets.IsCreated)
                contourOffsets.Dispose();
            if (contourPointIndices.IsCreated)
                contourPointIndices.Dispose();
        }
    }

    [Test]
    public void TriangulateContours_HolePreservesSubtractedArea()
    {
        var positions = new NativeArray<float3>(8, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 4, 8 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Allocator.Temp);
        var output = new NativeDetail(16, Allocator.TempJob);
        using var scratch = new TriangulationScratch();

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(4f, 0f, 0f);
            positions[2] = new float3(4f, 4f, 0f);
            positions[3] = new float3(0f, 4f, 0f);

            positions[4] = new float3(1f, 1f, 0f);
            positions[5] = new float3(1f, 3f, 0f);
            positions[6] = new float3(3f, 3f, 0f);
            positions[7] = new float3(3f, 1f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            var options = TriangulationOptions.Default;
            options.WindingRule = TriangulationWindingRule.EvenOdd;

            CoreResult result = Triangulation.TriangulateContours(in contours, ref output, scratch, in options);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.Greater(output.PrimitiveCount, 0);

            var compiled = output.Compile(Allocator.TempJob);
            try
            {
                Assert.IsTrue(compiled.IsTriangleOnlyTopology);
                float actualArea = ComputeCompiledArea(in compiled);
                Assert.AreEqual(12f, actualArea, 1e-4f);
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated)
                positions.Dispose();
            if (contourOffsets.IsCreated)
                contourOffsets.Dispose();
            if (contourPointIndices.IsCreated)
                contourPointIndices.Dispose();
        }
    }

    private static float PolygonArea2D(params float2[] polygon)
    {
        float area2 = 0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            float2 a = polygon[i];
            float2 b = polygon[(i + 1) % polygon.Length];
            area2 += a.x * b.y - b.x * a.y;
        }

        return math.abs(area2) * 0.5f;
    }

    private static float ComputeCompiledArea(in NativeCompiledDetail compiled)
    {
        Assert.AreEqual(CoreResult.Success, compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var pointPositions));

        float area = 0f;
        for (int primitiveIndex = 0; primitiveIndex < compiled.PrimitiveCount; primitiveIndex++)
        {
            int start = compiled.PrimitiveOffsetsDense[primitiveIndex];
            int aVertex = compiled.PrimitiveVerticesDense[start];
            int bVertex = compiled.PrimitiveVerticesDense[start + 1];
            int cVertex = compiled.PrimitiveVerticesDense[start + 2];

            float3 a = pointPositions[compiled.VertexToPointDense[aVertex]];
            float3 b = pointPositions[compiled.VertexToPointDense[bVertex]];
            float3 c = pointPositions[compiled.VertexToPointDense[cVertex]];

            area += TriangleArea2D(a, b, c);
        }

        return area;
    }

    private static float TriangleArea2D(float3 a, float3 b, float3 c)
    {
        float2 ab = new float2(b.x - a.x, b.y - a.y);
        float2 ac = new float2(c.x - a.x, c.y - a.y);
        return math.abs(ab.x * ac.y - ab.y * ac.x) * 0.5f;
    }
}
