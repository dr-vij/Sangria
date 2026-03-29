using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class SangriaMeshTriangulateRawTests
{
    [Test]
    public void TriangulateRaw_ConcavePolygon_ProducesCorrectTriangles()
    {
        var positions = new NativeArray<float3>(5, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 5 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4 }, Allocator.Temp);
        var outPositions = new NativeList<float3>(Allocator.TempJob);
        var outIndices = new NativeList<int>(Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(3f, 0f, 0f);
            positions[2] = new float3(3f, 3f, 0f);
            positions[3] = new float3(1.5f, 1f, 0f);
            positions[4] = new float3(0f, 3f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(5, outPositions.Length);
            Assert.IsTrue(outIndices.Length > 0);
            Assert.AreEqual(0, outIndices.Length % 3);

            float expectedArea = PolygonArea2D(
                new float2(0f, 0f),
                new float2(3f, 0f),
                new float2(3f, 3f),
                new float2(1.5f, 1f),
                new float2(0f, 3f));

            float actualArea = ComputeTriangulatedArea(outPositions, outIndices);
            Assert.AreEqual(expectedArea, actualArea, 1e-4f);
        }
        finally
        {
            outIndices.Dispose();
            outPositions.Dispose();
            positions.Dispose();
            contourOffsets.Dispose();
            contourPointIndices.Dispose();
        }
    }

    [Test]
    public void TriangulateRaw_WithHole_SubtractsHoleArea()
    {
        var positions = new NativeArray<float3>(8, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 4, 8 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Allocator.Temp);
        var outPositions = new NativeList<float3>(Allocator.TempJob);
        var outIndices = new NativeList<int>(Allocator.TempJob);

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

            CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices, in options);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.IsTrue(outIndices.Length > 0);
            Assert.AreEqual(0, outIndices.Length % 3);

            float actualArea = ComputeTriangulatedArea(outPositions, outIndices);
            Assert.AreEqual(12f, actualArea, 1e-4f);
        }
        finally
        {
            outIndices.Dispose();
            outPositions.Dispose();
            positions.Dispose();
            contourOffsets.Dispose();
            contourPointIndices.Dispose();
        }
    }

    [Test]
    public void TriangulateRaw_EmptyContours_ReturnsSuccessWithEmptyLists()
    {
        var positions = new NativeArray<float3>(0, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(0, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(0, Allocator.Temp);
        var outPositions = new NativeList<float3>(Allocator.TempJob);
        var outIndices = new NativeList<int>(Allocator.TempJob);

        try
        {
            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(0, outPositions.Length);
            Assert.AreEqual(0, outIndices.Length);
        }
        finally
        {
            outIndices.Dispose();
            outPositions.Dispose();
            positions.Dispose();
            contourOffsets.Dispose();
            contourPointIndices.Dispose();
        }
    }

    [Test]
    public void TriangulateRaw_SimpleTriangle_ProducesOneTriangle()
    {
        var positions = new NativeArray<float3>(3, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 3 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2 }, Allocator.Temp);
        var outPositions = new NativeList<float3>(Allocator.TempJob);
        var outIndices = new NativeList<int>(Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(2f, 0f, 0f);
            positions[2] = new float3(0f, 2f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(3, outPositions.Length);
            Assert.AreEqual(3, outIndices.Length);

            float actualArea = ComputeTriangulatedArea(outPositions, outIndices);
            Assert.AreEqual(2f, actualArea, 1e-4f);
        }
        finally
        {
            outIndices.Dispose();
            outPositions.Dispose();
            positions.Dispose();
            contourOffsets.Dispose();
            contourPointIndices.Dispose();
        }
    }

    [Test]
    public void TriangulateRaw_IndicesAreInRange()
    {
        var positions = new NativeArray<float3>(4, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 4 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3 }, Allocator.Temp);
        var outPositions = new NativeList<float3>(Allocator.TempJob);
        var outIndices = new NativeList<int>(Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(1f, 0f, 0f);
            positions[2] = new float3(1f, 1f, 0f);
            positions[3] = new float3(0f, 1f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices);

            Assert.AreEqual(CoreResult.Success, result);

            for (int i = 0; i < outIndices.Length; i++)
            {
                Assert.IsTrue(outIndices[i] >= 0 && outIndices[i] < outPositions.Length,
                    $"Index {outIndices[i]} at position {i} is out of range [0, {outPositions.Length})");
            }
        }
        finally
        {
            outIndices.Dispose();
            outPositions.Dispose();
            positions.Dispose();
            contourOffsets.Dispose();
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

    private static float ComputeTriangulatedArea(NativeList<float3> positions, NativeList<int> indices)
    {
        float area = 0f;
        int triCount = indices.Length / 3;
        for (int t = 0; t < triCount; t++)
        {
            float3 a = positions[indices[t * 3]];
            float3 b = positions[indices[t * 3 + 1]];
            float3 c = positions[indices[t * 3 + 2]];

            float2 ab = new float2(b.x - a.x, b.y - a.y);
            float2 ac = new float2(c.x - a.x, c.y - a.y);
            area += math.abs(ab.x * ac.y - ab.y * ac.x) * 0.5f;
        }

        return area;
    }
}
