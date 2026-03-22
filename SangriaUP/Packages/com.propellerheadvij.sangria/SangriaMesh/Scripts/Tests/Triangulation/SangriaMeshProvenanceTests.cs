using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;

public class SangriaMeshProvenanceTests
{
    [Test]
    public void Provenance_SimpleTriangle_AllIdentity()
    {
        var positions = new NativeArray<float3>(3, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 3 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2 }, Allocator.Temp);
        var output = new NativeDetail(8, Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(1f, 0f, 0f);
            positions[2] = new float3(0f, 1f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, out ProvenanceMap provenance, TriangulationOptions.Default);

            try
            {
                Assert.AreEqual(CoreResult.Success, result);
                Assert.IsTrue(provenance.IsCreated);
                Assert.AreEqual(3, provenance.OutputPointCount);
                Assert.AreEqual(3, provenance.SourcePointCount);

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    Assert.AreEqual(ProvenanceKind.Identity, record.Kind,
                        $"Point {i} should be Identity");
                    Assert.AreEqual(1, record.Count,
                        $"Point {i} should have exactly 1 source");
                    Assert.AreEqual(1.0f, record.W0, 1e-6f,
                        $"Point {i} weight should be 1.0");
                }

                // Verify source ids cover all input points
                bool[] seen = new bool[3];
                for (int i = 0; i < provenance.OutputPointCount; i++)
                    seen[provenance.Records[i].Src0] = true;

                for (int i = 0; i < 3; i++)
                    Assert.IsTrue(seen[i], $"Source point {i} not found in provenance");
            }
            finally
            {
                provenance.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }

    [Test]
    public void Provenance_ConcavePolygon_AllIdentity()
    {
        var positions = new NativeArray<float3>(5, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 5 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4 }, Allocator.Temp);
        var output = new NativeDetail(8, Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(3f, 0f, 0f);
            positions[2] = new float3(3f, 3f, 0f);
            positions[3] = new float3(1.5f, 1f, 0f);
            positions[4] = new float3(0f, 3f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, out ProvenanceMap provenance, TriangulationOptions.Default);

            try
            {
                Assert.AreEqual(CoreResult.Success, result);
                Assert.AreEqual(5, output.PointCount);
                Assert.AreEqual(5, provenance.OutputPointCount);

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    Assert.AreEqual(ProvenanceKind.Identity, record.Kind,
                        $"Point {i} should be Identity");
                    Assert.AreEqual(1, record.Count);
                    Assert.AreEqual(1.0f, record.W0, 1e-6f);
                }
            }
            finally
            {
                provenance.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }

    [Test]
    public void Provenance_HolePolygon_WeightSumInvariant()
    {
        var positions = new NativeArray<float3>(8, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 4, 8 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Allocator.Temp);
        var output = new NativeDetail(16, Allocator.TempJob);

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

            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, out ProvenanceMap provenance, in options);

            try
            {
                Assert.AreEqual(CoreResult.Success, result);
                Assert.IsTrue(provenance.IsCreated);
                Assert.Greater(provenance.OutputPointCount, 0);

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    Assert.Greater(record.Count, 0,
                        $"Point {i} should have at least 1 source");
                    Assert.LessOrEqual(record.Count, 4,
                        $"Point {i} should have at most 4 sources");

                    float weightSum = 0f;
                    if (record.Count > 0) weightSum += record.W0;
                    if (record.Count > 1) weightSum += record.W1;
                    if (record.Count > 2) weightSum += record.W2;
                    if (record.Count > 3) weightSum += record.W3;

                    Assert.AreEqual(1.0f, weightSum, 1e-4f,
                        $"Point {i} weight sum should be 1.0, got {weightSum}");

                    // Verify source ids are in valid range
                    if (record.Count > 0)
                        Assert.Less(record.Src0, provenance.SourcePointCount,
                            $"Point {i} Src0 out of range");
                    if (record.Count > 1)
                        Assert.Less(record.Src1, provenance.SourcePointCount,
                            $"Point {i} Src1 out of range");
                    if (record.Count > 2)
                        Assert.Less(record.Src2, provenance.SourcePointCount,
                            $"Point {i} Src2 out of range");
                    if (record.Count > 3)
                        Assert.Less(record.Src3, provenance.SourcePointCount,
                            $"Point {i} Src3 out of range");
                }
            }
            finally
            {
                provenance.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }

    [Test]
    public void Provenance_WithoutProvenance_BackwardCompatible()
    {
        var positions = new NativeArray<float3>(3, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 3 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2 }, Allocator.Temp);
        var output = new NativeDetail(8, Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(1f, 0f, 0f);
            positions[2] = new float3(0f, 1f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, TriangulationOptions.Default);

            Assert.AreEqual(CoreResult.Success, result);
            Assert.AreEqual(3, output.PointCount);
            Assert.AreEqual(1, output.PrimitiveCount);
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }

    [Test]
    public void Provenance_IdentityPassthrough_ReconstructsPositionFromProvenance()
    {
        var positions = new NativeArray<float3>(3, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 3 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2 }, Allocator.Temp);
        var output = new NativeDetail(8, Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(1f, 0f, 0f);
            positions[2] = new float3(0f, 1f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, out ProvenanceMap provenance, TriangulationOptions.Default);

            try
            {
                Assert.AreEqual(CoreResult.Success, result);
                Assert.AreEqual(CoreResult.Success,
                    output.TryGetPointAccessor<float3>(AttributeID.Position, out var outPositions));

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    float3 reconstructed = float3.zero;
                    if (record.Count > 0) reconstructed += record.W0 * positions[record.Src0];
                    if (record.Count > 1) reconstructed += record.W1 * positions[record.Src1];
                    if (record.Count > 2) reconstructed += record.W2 * positions[record.Src2];
                    if (record.Count > 3) reconstructed += record.W3 * positions[record.Src3];

                    float3 actual = outPositions[i];
                    Assert.AreEqual(actual.x, reconstructed.x, 1e-4f,
                        $"Point {i} x mismatch: actual={actual.x}, reconstructed={reconstructed.x}");
                    Assert.AreEqual(actual.y, reconstructed.y, 1e-4f,
                        $"Point {i} y mismatch: actual={actual.y}, reconstructed={reconstructed.y}");
                }
            }
            finally
            {
                provenance.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }

    [Test]
    public void ProvenanceRecord_Combine_CoalescesDuplicates()
    {
        var a = ProvenanceRecord.Identity(5);
        var b = ProvenanceRecord.Identity(5);

        var combined = ProvenanceRecord.Combine(in a, in b, ProvenanceKind.Merge);

        Assert.AreEqual(1, combined.Count);
        Assert.AreEqual(5, combined.Src0);
        Assert.AreEqual(1.0f, combined.W0, 1e-6f);
        Assert.AreEqual(ProvenanceKind.Merge, combined.Kind);
    }

    [Test]
    public void ProvenanceRecord_Combine_TruncatesToFour()
    {
        var a = new ProvenanceRecord
        {
            Src0 = 0, W0 = 0.25f,
            Src1 = 1, W1 = 0.25f,
            Src2 = 2, W2 = 0.25f,
            Src3 = 3, W3 = 0.25f,
            Count = 4,
            Kind = ProvenanceKind.Intersection
        };

        var b = new ProvenanceRecord
        {
            Src0 = 4, W0 = 0.25f,
            Src1 = 5, W1 = 0.25f,
            Src2 = 6, W2 = 0.25f,
            Src3 = 7, W3 = 0.25f,
            Count = 4,
            Kind = ProvenanceKind.Intersection
        };

        var combined = ProvenanceRecord.Combine(in a, in b, ProvenanceKind.Merge);

        Assert.LessOrEqual(combined.Count, 4);
        Assert.AreEqual(ProvenanceKind.Merge, combined.Kind);

        float weightSum = 0f;
        if (combined.Count > 0) weightSum += combined.W0;
        if (combined.Count > 1) weightSum += combined.W1;
        if (combined.Count > 2) weightSum += combined.W2;
        if (combined.Count > 3) weightSum += combined.W3;

        Assert.AreEqual(1.0f, weightSum, 1e-5f, "Weight sum should be normalized to 1.0");
    }

    [Test]
    public void ProvenanceRecord_Combine_SortsBySourceIdAscending()
    {
        var a = new ProvenanceRecord
        {
            Src0 = 7, W0 = 0.5f,
            Src1 = 3, W1 = 0.5f,
            Count = 2,
            Kind = ProvenanceKind.Intersection
        };

        var b = new ProvenanceRecord
        {
            Src0 = 1, W0 = 0.5f,
            Src1 = 5, W1 = 0.5f,
            Count = 2,
            Kind = ProvenanceKind.Intersection
        };

        var combined = ProvenanceRecord.Combine(in a, in b, ProvenanceKind.Merge);

        Assert.AreEqual(4, combined.Count);
        // After truncation, should be sorted by sourceId ascending
        Assert.Less(combined.Src0, combined.Src1, "Sources should be sorted by id ascending");
        if (combined.Count > 2)
            Assert.Less(combined.Src1, combined.Src2, "Sources should be sorted by id ascending");
        if (combined.Count > 3)
            Assert.Less(combined.Src2, combined.Src3, "Sources should be sorted by id ascending");
    }

    [Test]
    public void ProvenanceRecord_Intersection_NormalizesWeights()
    {
        var record = ProvenanceRecord.Intersection(0, 0.3f, 1, 0.2f, 2, 0.3f, 3, 0.2f);

        Assert.AreEqual(4, record.Count);
        Assert.AreEqual(ProvenanceKind.Intersection, record.Kind);

        float sum = record.W0 + record.W1 + record.W2 + record.W3;
        Assert.AreEqual(1.0f, sum, 1e-5f, "Intersection weights should be normalized");
    }

    [Test]
    public void ProvenanceRecord_CombineWeighted_FlattensCompositeRecords()
    {
        // Record A is a previous intersection result with 2 sources
        var a = new ProvenanceRecord
        {
            Src0 = 0, W0 = 0.6f,
            Src1 = 1, W1 = 0.4f,
            Count = 2,
            Kind = ProvenanceKind.Intersection
        };
        var b = ProvenanceRecord.Identity(2);
        var c = ProvenanceRecord.Identity(3);
        var d = ProvenanceRecord.Identity(4);

        // Weights: a=0.5, b=0.2, c=0.2, d=0.1
        var result = ProvenanceRecord.CombineWeighted(
            in a, 0.5f,
            in b, 0.2f,
            in c, 0.2f,
            in d, 0.1f,
            ProvenanceKind.Intersection);

        // Pre-calculated flatten:
        //   From A (scale 0.5): Src0=0, W=0.6*0.5=0.30; Src1=1, W=0.4*0.5=0.20
        //   From B (scale 0.2): Src2=2, W=1.0*0.2=0.20
        //   From C (scale 0.2): Src3=3, W=1.0*0.2=0.20
        //   From D (scale 0.1): Src4=4, W=1.0*0.1=0.10
        // 5 unique sources. Sort by weight desc: {0:0.30, 1:0.20, 2:0.20, 3:0.20, 4:0.10}
        // Truncate to 4: drop Src4 (0.10). Remaining sum = 0.90
        // Sort by sourceId asc: {0, 1, 2, 3}
        // Normalize (÷0.90): {0: 0.3333, 1: 0.2222, 2: 0.2222, 3: 0.2222}
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(ProvenanceKind.Intersection, result.Kind);

        Assert.AreEqual(0, result.Src0);
        Assert.AreEqual(1, result.Src1);
        Assert.AreEqual(2, result.Src2);
        Assert.AreEqual(3, result.Src3);

        float expectedW0 = 0.30f / 0.90f; // 0.3333...
        float expectedW1 = 0.20f / 0.90f; // 0.2222...
        Assert.AreEqual(expectedW0, result.W0, 1e-5f, "W0");
        Assert.AreEqual(expectedW1, result.W1, 1e-5f, "W1");
        Assert.AreEqual(expectedW1, result.W2, 1e-5f, "W2");
        Assert.AreEqual(expectedW1, result.W3, 1e-5f, "W3");

        float weightSum = result.W0 + result.W1 + result.W2 + result.W3;
        Assert.AreEqual(1.0f, weightSum, 1e-5f, "Weight sum");
    }

    [Test]
    public void ProvenanceRecord_CombineWeighted_CoalescesDuplicateSources()
    {
        // Both a and b reference source 0, should coalesce
        var a = new ProvenanceRecord
        {
            Src0 = 0, W0 = 0.7f,
            Src1 = 1, W1 = 0.3f,
            Count = 2,
            Kind = ProvenanceKind.Intersection
        };
        var b = new ProvenanceRecord
        {
            Src0 = 0, W0 = 0.5f,
            Src1 = 2, W1 = 0.5f,
            Count = 2,
            Kind = ProvenanceKind.Intersection
        };
        var c = ProvenanceRecord.Identity(3);
        var d = ProvenanceRecord.Identity(4);

        // Weights: a=0.4, b=0.3, c=0.2, d=0.1
        var result = ProvenanceRecord.CombineWeighted(
            in a, 0.4f,
            in b, 0.3f,
            in c, 0.2f,
            in d, 0.1f,
            ProvenanceKind.Intersection);

        // Pre-calculated flatten:
        //   From A (scale 0.4): Src0=0, W=0.7*0.4=0.28; Src1=1, W=0.3*0.4=0.12
        //   From B (scale 0.3): Src0=0, W=0.5*0.3=0.15; Src1=2, W=0.5*0.3=0.15
        //   From C (scale 0.2): Src3=3, W=1.0*0.2=0.20
        //   From D (scale 0.1): Src4=4, W=1.0*0.1=0.10
        // After coalesce Src0: 0.28 + 0.15 = 0.43
        // Unique sources: {0:0.43, 1:0.12, 2:0.15, 3:0.20, 4:0.10}
        // Sort by weight desc: {0:0.43, 3:0.20, 2:0.15, 1:0.12, 4:0.10}
        // Truncate to 4: drop Src4 (0.10). Remaining sum = 0.90
        // Sort by sourceId asc: {0, 1, 2, 3}
        // Normalize (÷0.90): {0: 0.4778, 1: 0.1333, 2: 0.1667, 3: 0.2222}
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(ProvenanceKind.Intersection, result.Kind);

        Assert.AreEqual(0, result.Src0);
        Assert.AreEqual(1, result.Src1);
        Assert.AreEqual(2, result.Src2);
        Assert.AreEqual(3, result.Src3);

        Assert.AreEqual(0.43f / 0.90f, result.W0, 1e-5f, "W0 (coalesced src 0)");
        Assert.AreEqual(0.12f / 0.90f, result.W1, 1e-5f, "W1");
        Assert.AreEqual(0.15f / 0.90f, result.W2, 1e-5f, "W2");
        Assert.AreEqual(0.20f / 0.90f, result.W3, 1e-5f, "W3");

        float weightSum = result.W0 + result.W1 + result.W2 + result.W3;
        Assert.AreEqual(1.0f, weightSum, 1e-5f, "Weight sum");
    }

    [Test]
    public void Provenance_IntersectingContours_GeneratedPointsHaveMultipleSources()
    {
        // Two overlapping squares - creates intersection points
        var positions = new NativeArray<float3>(8, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 4, 8 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Allocator.Temp);
        var output = new NativeDetail(32, Allocator.TempJob);

        try
        {
            // First square
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(3f, 0f, 0f);
            positions[2] = new float3(3f, 3f, 0f);
            positions[3] = new float3(0f, 3f, 0f);

            // Second overlapping square
            positions[4] = new float3(1f, 1f, 0f);
            positions[5] = new float3(4f, 1f, 0f);
            positions[6] = new float3(4f, 4f, 0f);
            positions[7] = new float3(1f, 4f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            var options = TriangulationOptions.Default;
            options.WindingRule = TriangulationWindingRule.NonZero;

            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, out ProvenanceMap provenance, in options);

            try
            {
                Assert.AreEqual(CoreResult.Success, result);
                Assert.IsTrue(provenance.IsCreated);
                Assert.Greater(provenance.OutputPointCount, 0);

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    Assert.Greater(record.Count, 0);
                    Assert.LessOrEqual(record.Count, 4);

                    float weightSum = 0f;
                    if (record.Count > 0) weightSum += record.W0;
                    if (record.Count > 1) weightSum += record.W1;
                    if (record.Count > 2) weightSum += record.W2;
                    if (record.Count > 3) weightSum += record.W3;

                    Assert.AreEqual(1.0f, weightSum, 1e-4f,
                        $"Point {i} (kind={record.Kind}) weight sum should be 1.0, got {weightSum}");
                }
            }
            finally
            {
                provenance.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }

    [Test]
    public void Provenance_CascadingIntersections_InvariantsAndIdentityReconstruction()
    {
        // 3 overlapping squares — creates cascading intersections where some output
        // vertices are derived from >4 unique sources, triggering truncation.
        // We verify:
        //   - All provenance invariants (weight sum, source validity, count, ordering)
        //   - Identity points reconstruct position exactly (tight tolerance)
        //   - Non-identity points: invariants only (truncation error is verified
        //     analytically by ProvenanceRecord_CombineWeighted_* unit tests)
        var positions = new NativeArray<float3>(12, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 4, 8, 12 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(
            new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }, Allocator.Temp);
        var output = new NativeDetail(64, Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(3f, 0f, 0f);
            positions[2] = new float3(3f, 3f, 0f);
            positions[3] = new float3(0f, 3f, 0f);

            positions[4] = new float3(1f, 1f, 0f);
            positions[5] = new float3(4f, 1f, 0f);
            positions[6] = new float3(4f, 4f, 0f);
            positions[7] = new float3(1f, 4f, 0f);

            positions[8] = new float3(2f, 0.5f, 0f);
            positions[9] = new float3(5f, 0.5f, 0f);
            positions[10] = new float3(5f, 3.5f, 0f);
            positions[11] = new float3(2f, 3.5f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            var options = TriangulationOptions.Default;
            options.WindingRule = TriangulationWindingRule.NonZero;

            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, out ProvenanceMap provenance, in options);

            try
            {
                Assert.AreEqual(CoreResult.Success, result);
                Assert.AreEqual(CoreResult.Success,
                    output.TryGetPointAccessor<float3>(AttributeID.Position, out var outPositions));

                int identityCount = 0;
                int intersectionCount = 0;

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];

                    // Invariant: count in [1, 4]
                    Assert.Greater(record.Count, 0,
                        $"Point {i}: Count must be > 0");
                    Assert.LessOrEqual(record.Count, 4,
                        $"Point {i}: Count must be <= 4");

                    // Invariant: weight sum = 1.0
                    float weightSum = 0f;
                    if (record.Count > 0) weightSum += record.W0;
                    if (record.Count > 1) weightSum += record.W1;
                    if (record.Count > 2) weightSum += record.W2;
                    if (record.Count > 3) weightSum += record.W3;
                    Assert.AreEqual(1.0f, weightSum, 1e-4f,
                        $"Point {i} (kind={record.Kind}) weight sum = {weightSum}");

                    // Invariant: all source ids valid
                    if (record.Count > 0) Assert.Less(record.Src0, 12,
                        $"Point {i}: Src0={record.Src0} out of range");
                    if (record.Count > 1) Assert.Less(record.Src1, 12,
                        $"Point {i}: Src1={record.Src1} out of range");
                    if (record.Count > 2) Assert.Less(record.Src2, 12,
                        $"Point {i}: Src2={record.Src2} out of range");
                    if (record.Count > 3) Assert.Less(record.Src3, 12,
                        $"Point {i}: Src3={record.Src3} out of range");

                    // Invariant: sources sorted by id ascending
                    if (record.Count > 1) Assert.LessOrEqual(record.Src0, record.Src1,
                        $"Point {i}: sources not sorted (Src0={record.Src0} > Src1={record.Src1})");
                    if (record.Count > 2) Assert.LessOrEqual(record.Src1, record.Src2,
                        $"Point {i}: sources not sorted (Src1={record.Src1} > Src2={record.Src2})");
                    if (record.Count > 3) Assert.LessOrEqual(record.Src2, record.Src3,
                        $"Point {i}: sources not sorted (Src2={record.Src2} > Src3={record.Src3})");

                    // Invariant: all weights positive
                    if (record.Count > 0) Assert.Greater(record.W0, 0f,
                        $"Point {i}: W0 must be positive");
                    if (record.Count > 1) Assert.Greater(record.W1, 0f,
                        $"Point {i}: W1 must be positive");
                    if (record.Count > 2) Assert.Greater(record.W2, 0f,
                        $"Point {i}: W2 must be positive");
                    if (record.Count > 3) Assert.Greater(record.W3, 0f,
                        $"Point {i}: W3 must be positive");

                    // Identity points: exact position reconstruction (no truncation possible)
                    if (record.Kind == ProvenanceKind.Identity)
                    {
                        identityCount++;
                        Assert.AreEqual(1, record.Count);
                        float3 actual = outPositions[i];
                        float3 source = positions[record.Src0];
                        Assert.AreEqual(actual.x, source.x, 1e-5f,
                            $"Identity point {i}: x mismatch");
                        Assert.AreEqual(actual.y, source.y, 1e-5f,
                            $"Identity point {i}: y mismatch");
                    }
                    else
                    {
                        intersectionCount++;
                    }
                }

                // Must have both identity and generated points
                Assert.Greater(identityCount, 0, "Expected some identity points");
                Assert.Greater(intersectionCount, 0, "Expected some generated points from intersections");
            }
            finally
            {
                provenance.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }

    [Test]
    public void ProvenanceRecord_CombineWeighted_NoTruncation_ExactValues()
    {
        // 4 Identity records → CombineWeighted with 4 sources, no truncation needed
        var a = ProvenanceRecord.Identity(0);
        var b = ProvenanceRecord.Identity(1);
        var c = ProvenanceRecord.Identity(2);
        var d = ProvenanceRecord.Identity(3);

        // Weights: 0.4, 0.3, 0.2, 0.1
        var result = ProvenanceRecord.CombineWeighted(
            in a, 0.4f, in b, 0.3f, in c, 0.2f, in d, 0.1f,
            ProvenanceKind.Intersection);

        // Pre-calculated: 4 unique sources, no coalescing, no truncation.
        // Sum = 1.0, normalize is identity.
        // Sorted by sourceId asc: {0:0.4, 1:0.3, 2:0.2, 3:0.1}
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(0, result.Src0);
        Assert.AreEqual(1, result.Src1);
        Assert.AreEqual(2, result.Src2);
        Assert.AreEqual(3, result.Src3);
        Assert.AreEqual(0.4f, result.W0, 1e-6f);
        Assert.AreEqual(0.3f, result.W1, 1e-6f);
        Assert.AreEqual(0.2f, result.W2, 1e-6f);
        Assert.AreEqual(0.1f, result.W3, 1e-6f);

        // Position reconstruction: exact for any set of positions
        var p0 = new float3(1f, 2f, 0f);
        var p1 = new float3(5f, 0f, 0f);
        var p2 = new float3(3f, 7f, 0f);
        var p3 = new float3(9f, 1f, 0f);
        float3 expected = 0.4f * p0 + 0.3f * p1 + 0.2f * p2 + 0.1f * p3;
        float3 reconstructed = result.W0 * p0 + result.W1 * p1 + result.W2 * p2 + result.W3 * p3;
        Assert.AreEqual(expected.x, reconstructed.x, 1e-5f);
        Assert.AreEqual(expected.y, reconstructed.y, 1e-5f);
    }

    [Test]
    public void Provenance_IntersectingContours_PositionReconstructionFromProvenance()
    {
        // Two overlapping squares with position reconstruction check
        var positions = new NativeArray<float3>(8, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(new[] { 0, 4, 8 }, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, Allocator.Temp);
        var output = new NativeDetail(32, Allocator.TempJob);

        try
        {
            positions[0] = new float3(0f, 0f, 0f);
            positions[1] = new float3(3f, 0f, 0f);
            positions[2] = new float3(3f, 3f, 0f);
            positions[3] = new float3(0f, 3f, 0f);

            positions[4] = new float3(1f, 1f, 0f);
            positions[5] = new float3(4f, 1f, 0f);
            positions[6] = new float3(4f, 4f, 0f);
            positions[7] = new float3(1f, 4f, 0f);

            var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            var options = TriangulationOptions.Default;
            options.WindingRule = TriangulationWindingRule.NonZero;

            CoreResult result = Triangulation.TriangulateContours(
                in contours, ref output, out ProvenanceMap provenance, in options);

            try
            {
                Assert.AreEqual(CoreResult.Success, result);
                Assert.AreEqual(CoreResult.Success,
                    output.TryGetPointAccessor<float3>(AttributeID.Position, out var outPositions));

                for (int i = 0; i < provenance.OutputPointCount; i++)
                {
                    var record = provenance.Records[i];
                    float3 reconstructed = float3.zero;
                    if (record.Count > 0) reconstructed += record.W0 * positions[record.Src0];
                    if (record.Count > 1) reconstructed += record.W1 * positions[record.Src1];
                    if (record.Count > 2) reconstructed += record.W2 * positions[record.Src2];
                    if (record.Count > 3) reconstructed += record.W3 * positions[record.Src3];

                    float3 actual = outPositions[i];
                    Assert.AreEqual(actual.x, reconstructed.x, 0.05f,
                        $"Point {i} (kind={record.Kind}) x: actual={actual.x}, reconstructed={reconstructed.x}");
                    Assert.AreEqual(actual.y, reconstructed.y, 0.05f,
                        $"Point {i} (kind={record.Kind}) y: actual={actual.y}, reconstructed={reconstructed.y}");
                }
            }
            finally
            {
                provenance.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (contourOffsets.IsCreated) contourOffsets.Dispose();
            if (contourPointIndices.IsCreated) contourPointIndices.Dispose();
        }
    }
}
