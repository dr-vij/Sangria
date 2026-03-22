using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LibTessDotNet;
using NUnit.Framework;
using SangriaMesh;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using TessContourOrientation = LibTessDotNet.ContourOrientation;
using TessElementType = LibTessDotNet.ElementType;
using TessWindingRule = LibTessDotNet.WindingRule;

public class SangriaMeshTriangulationOracleTests
{
    private const int ElementSize = 3;
    private const float PositionEpsilon = 1e-4f;

    private sealed class DatContour
    {
        public readonly List<float3> Points = new List<float3>();
        public TessContourOrientation ForceOrientation = TessContourOrientation.Original;
    }

    private readonly struct EpsilonTriangle
    {
        public readonly float3 V0;
        public readonly float3 V1;
        public readonly float3 V2;

        public EpsilonTriangle(float3 a, float3 b, float3 c)
        {
            if (CompareLex(a, b) > 0)
            {
                (a, b) = (b, a);
            }

            if (CompareLex(b, c) > 0)
            {
                (b, c) = (c, b);
            }

            if (CompareLex(a, b) > 0)
            {
                (a, b) = (b, a);
            }

            V0 = a;
            V1 = b;
            V2 = c;
        }
    }

    public static IEnumerable<TestCaseData> GetOracleCases()
    {
        string dataDirectory = GetTestDataDirectory();
        if (!Directory.Exists(dataDirectory))
            yield break;

        foreach (string testDatPath in Directory.EnumerateFiles(dataDirectory, "*.testdat")
                     .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(testDatPath);
            string datPath = Path.Combine(dataDirectory, name + ".dat");

            foreach (TessWindingRule winding in Enum.GetValues(typeof(TessWindingRule)))
            {
                yield return new TestCaseData(datPath, testDatPath, winding)
                    .SetName($"Oracle_{name}_{winding}");
            }
        }
    }

    [TestCaseSource(nameof(GetOracleCases))]
    [Category("Oracle")]
    public void TriangulateContours_OracleData_MatchesLibTessReference(
        string datPath,
        string testDatPath,
        TessWindingRule winding)
    {
        Assert.IsTrue(File.Exists(datPath), $"Missing contour source file: {datPath}");
        Assert.IsTrue(File.Exists(testDatPath), $"Missing oracle file: {testDatPath}");

        List<DatContour> contours = ParseDatContours(datPath);
        Assert.IsTrue(contours.Count > 0, $"No contours parsed from: {datPath}");

        int[] expectedIndices = ParseTestData(winding, ElementSize, testDatPath);
        Assert.IsNotNull(expectedIndices, $"Oracle section not found: {winding} {ElementSize} in {testDatPath}");

        Tess tess = BuildLibTess(contours, winding);
        CollectionAssert.AreEqual(
            expectedIndices,
            tess.Elements,
            $"LibTess output diverged from oracle file for {Path.GetFileName(testDatPath)} / {winding}");

        List<EpsilonTriangle> expectedTriangles = BuildTrianglesFromLibTess(tess.Vertices, expectedIndices);
        List<EpsilonTriangle> nativeTriangles = TriangulateNativeAndBuildTriangles(contours, MapWindingRule(winding));

        AssertTriangleMultisetEqual(
            expectedTriangles,
            nativeTriangles,
            $"Native triangulation mismatch for {Path.GetFileName(testDatPath)} / {winding}");
    }

    private static string GetTestDataDirectory()
    {
        string fromCurrentDirectory = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            "Packages",
            "com.propellerheadvij.sangria",
            "SangriaMesh",
            "ThirdParty",
            "LibTessDotNet",
            "TestData"));

        if (Directory.Exists(fromCurrentDirectory))
            return fromCurrentDirectory;

        return Path.GetFullPath(Path.Combine(
            Application.dataPath,
            "..",
            "Packages",
            "com.propellerheadvij.sangria",
            "SangriaMesh",
            "ThirdParty",
            "LibTessDotNet",
            "TestData"));
    }

    private static List<DatContour> ParseDatContours(string datPath)
    {
        var contours = new List<DatContour>();
        var currentPoints = new List<float3>();
        TessContourOrientation currentOrientation = TessContourOrientation.Original;

        foreach (string rawLine in File.ReadLines(datPath))
        {
            string line = rawLine.Trim();

            if (string.IsNullOrEmpty(line))
            {
                FlushContour(contours, currentPoints, currentOrientation);
                currentOrientation = TessContourOrientation.Original;
                continue;
            }

            if (line.StartsWith("//", StringComparison.Ordinal) ||
                line.StartsWith("#", StringComparison.Ordinal) ||
                line.StartsWith(";", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("force", StringComparison.OrdinalIgnoreCase))
            {
                currentOrientation = ParseForceOrientation(line);
                continue;
            }

            if (line.StartsWith("color", StringComparison.OrdinalIgnoreCase))
                continue;

            string[] xyz = line.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (xyz.Length == 0)
                continue;

            float x = ParseFloat(xyz[0]);
            float y = xyz.Length >= 2 ? ParseFloat(xyz[1]) : 0.0f;
            float z = xyz.Length >= 3 ? ParseFloat(xyz[2]) : 0.0f;
            currentPoints.Add(new float3(x, y, z));
        }

        FlushContour(contours, currentPoints, currentOrientation);
        return contours;
    }

    private static void FlushContour(List<DatContour> contours, List<float3> points, TessContourOrientation orientation)
    {
        if (points.Count == 0)
            return;

        var contour = new DatContour { ForceOrientation = orientation };
        contour.Points.AddRange(points);
        contours.Add(contour);
        points.Clear();
    }

    private static TessContourOrientation ParseForceOrientation(string line)
    {
        string[] parts = line.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            if (string.Equals(parts[1], "cw", StringComparison.OrdinalIgnoreCase))
                return TessContourOrientation.Clockwise;
            if (string.Equals(parts[1], "ccw", StringComparison.OrdinalIgnoreCase))
                return TessContourOrientation.CounterClockwise;
        }

        return TessContourOrientation.Original;
    }

    private static float ParseFloat(string token)
    {
        return float.Parse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
    }

    private static int[] ParseTestData(TessWindingRule winding, int elementSize, string testDataPath)
    {
        var lines = new List<string>();
        bool found = false;

        foreach (string rawLine in File.ReadLines(testDataPath))
        {
            string line = rawLine.Trim();

            if (found && string.IsNullOrEmpty(line))
                break;

            if (found)
                lines.Add(line);

            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                string.Equals(parts[0], winding.ToString(), StringComparison.Ordinal) &&
                int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedElementSize) &&
                parsedElementSize == elementSize)
            {
                found = true;
            }
        }

        if (!found)
            return null;

        var indices = new List<int>();
        foreach (string line in lines)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != elementSize)
                continue;

            foreach (string part in parts)
            {
                if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                    indices.Add(index);
            }
        }

        return indices.ToArray();
    }

    private static Tess BuildLibTess(IReadOnlyList<DatContour> contours, TessWindingRule winding)
    {
        var tess = new Tess();

        for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
        {
            DatContour contour = contours[contourIndex];
            var vertices = new ContourVertex[contour.Points.Count];

            for (int i = 0; i < contour.Points.Count; i++)
            {
                float3 p = contour.Points[i];
                vertices[i] = new ContourVertex(new Vec3(p.x, p.y, p.z));
            }

            tess.AddContour(vertices, contour.ForceOrientation);
        }

        tess.Tessellate(winding, TessElementType.Polygons, ElementSize);
        return tess;
    }

    private static List<EpsilonTriangle> TriangulateNativeAndBuildTriangles(IReadOnlyList<DatContour> sourceContours, TriangulationWindingRule winding)
    {
        var contours = new List<float3[]>(sourceContours.Count);
        int totalPoints = 0;

        for (int i = 0; i < sourceContours.Count; i++)
        {
            var points = new List<float3>(sourceContours[i].Points);
            ApplyForcedOrientation(points, sourceContours[i].ForceOrientation);

            if (points.Count < 3)
                continue;

            float3[] contour = points.ToArray();
            contours.Add(contour);
            totalPoints += contour.Length;
        }

        if (contours.Count == 0)
            return new List<EpsilonTriangle>();

        var positions = new NativeArray<float3>(totalPoints, Allocator.Temp);
        var contourOffsets = new NativeArray<int>(contours.Count + 1, Allocator.Temp);
        var contourPointIndices = new NativeArray<int>(totalPoints, Allocator.Temp);
        var output = new NativeDetail(math.max(64, totalPoints * 4), Allocator.TempJob);
        using var scratch = new TriangulationScratch();

        try
        {
            int cursor = 0;
            for (int contourIndex = 0; contourIndex < contours.Count; contourIndex++)
            {
                contourOffsets[contourIndex] = cursor;
                float3[] contour = contours[contourIndex];
                for (int i = 0; i < contour.Length; i++)
                {
                    positions[cursor] = contour[i];
                    contourPointIndices[cursor] = cursor;
                    cursor++;
                }
            }
            contourOffsets[contours.Count] = cursor;

            var contourSet = new NativeContourSet(positions, contourOffsets, contourPointIndices);
            var options = TriangulationOptions.Default;
            options.WindingRule = winding;
            options.ContourOrientation = TriangulationContourOrientation.Original;

            CoreResult result = Triangulation.TriangulateContours(in contourSet, ref output, scratch, in options);
            Assert.AreEqual(CoreResult.Success, result);

            if (output.PrimitiveCount == 0)
                return new List<EpsilonTriangle>();

            var compiled = output.Compile(Allocator.TempJob);
            try
            {
                return BuildTrianglesFromCompiled(in compiled);
            }
            finally
            {
                compiled.Dispose();
            }
        }
        finally
        {
            output.Dispose();
            positions.Dispose();
            contourOffsets.Dispose();
            contourPointIndices.Dispose();
        }
    }

    private static void ApplyForcedOrientation(List<float3> points, TessContourOrientation orientation)
    {
        if (orientation == TessContourOrientation.Original || points.Count < 3)
            return;

        float signedArea = SignedArea(points);
        bool reverse = (orientation == TessContourOrientation.Clockwise && signedArea < 0.0f) ||
                       (orientation == TessContourOrientation.CounterClockwise && signedArea > 0.0f);

        if (reverse)
            points.Reverse();
    }

    private static float SignedArea(IReadOnlyList<float3> points)
    {
        float area = 0.0f;
        for (int i = 0; i < points.Count; i++)
        {
            float3 a = points[i];
            float3 b = points[(i + 1) % points.Count];
            area += a.x * b.y;
            area -= a.y * b.x;
        }

        return 0.5f * area;
    }

    private static TriangulationWindingRule MapWindingRule(TessWindingRule winding)
    {
        return winding switch
        {
            TessWindingRule.EvenOdd => TriangulationWindingRule.EvenOdd,
            TessWindingRule.NonZero => TriangulationWindingRule.NonZero,
            TessWindingRule.Positive => TriangulationWindingRule.Positive,
            TessWindingRule.Negative => TriangulationWindingRule.Negative,
            TessWindingRule.AbsGeqTwo => TriangulationWindingRule.AbsGeqTwo,
            _ => TriangulationWindingRule.EvenOdd
        };
    }

    private static List<EpsilonTriangle> BuildTrianglesFromLibTess(ContourVertex[] vertices, int[] indices)
    {
        var triangles = new List<EpsilonTriangle>(indices.Length / 3);

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int ia = indices[i];
            int ib = indices[i + 1];
            int ic = indices[i + 2];

            if ((uint)ia >= (uint)vertices.Length || (uint)ib >= (uint)vertices.Length || (uint)ic >= (uint)vertices.Length)
                continue;

            float3 a = ToFloat3(vertices[ia].Position);
            float3 b = ToFloat3(vertices[ib].Position);
            float3 c = ToFloat3(vertices[ic].Position);
            triangles.Add(new EpsilonTriangle(a, b, c));
        }

        return triangles;
    }

    private static List<EpsilonTriangle> BuildTrianglesFromCompiled(in NativeCompiledDetail compiled)
    {
        Assert.AreEqual(
            CoreResult.Success,
            compiled.TryGetAttributeAccessor<float3>(MeshDomain.Point, AttributeID.Position, out var pointPositions));

        var triangles = new List<EpsilonTriangle>(compiled.PrimitiveCount);

        for (int primitiveIndex = 0; primitiveIndex < compiled.PrimitiveCount; primitiveIndex++)
        {
            int start = compiled.PrimitiveOffsetsDense[primitiveIndex];
            int end = primitiveIndex + 1 < compiled.PrimitiveCount
                ? compiled.PrimitiveOffsetsDense[primitiveIndex + 1]
                : compiled.PrimitiveVerticesDense.Length;

            int length = end - start;
            if (length < 3)
                continue;

            int aVertex = compiled.PrimitiveVerticesDense[start];
            for (int offset = 1; offset + 1 < length; offset++)
            {
                int bVertex = compiled.PrimitiveVerticesDense[start + offset];
                int cVertex = compiled.PrimitiveVerticesDense[start + offset + 1];

                int aPoint = compiled.VertexToPointDense[aVertex];
                int bPoint = compiled.VertexToPointDense[bVertex];
                int cPoint = compiled.VertexToPointDense[cVertex];

                if ((uint)aPoint >= (uint)pointPositions.Length ||
                    (uint)bPoint >= (uint)pointPositions.Length ||
                    (uint)cPoint >= (uint)pointPositions.Length)
                {
                    continue;
                }

                triangles.Add(new EpsilonTriangle(
                    pointPositions[aPoint],
                    pointPositions[bPoint],
                    pointPositions[cPoint]));
            }
        }

        return triangles;
    }

    private static float3 ToFloat3(in Vec3 value)
    {
        return new float3((float)value.X, (float)value.Y, (float)value.Z);
    }

    private static void AssertTriangleMultisetEqual(
        List<EpsilonTriangle> expected,
        List<EpsilonTriangle> actual,
        string message)
    {
        Assert.AreEqual(expected.Count, actual.Count, $"{message}. Triangle count mismatch.");

        var used = new bool[actual.Count];

        for (int i = 0; i < expected.Count; i++)
        {
            EpsilonTriangle expectedTriangle = expected[i];
            int matchedIndex = -1;

            for (int j = 0; j < actual.Count; j++)
            {
                if (used[j])
                    continue;

                if (TrianglesApproximatelyEqual(expectedTriangle, actual[j]))
                {
                    matchedIndex = j;
                    break;
                }
            }

            Assert.AreNotEqual(-1, matchedIndex, $"{message}. No epsilon-match for expected triangle index {i}.");
            used[matchedIndex] = true;
        }
    }

    private static bool TrianglesApproximatelyEqual(EpsilonTriangle lhs, EpsilonTriangle rhs)
    {
        return ApproximatelyEqual(lhs.V0, rhs.V0) &&
               ApproximatelyEqual(lhs.V1, rhs.V1) &&
               ApproximatelyEqual(lhs.V2, rhs.V2);
    }

    private static bool ApproximatelyEqual(float3 a, float3 b)
    {
        return math.abs(a.x - b.x) <= PositionEpsilon &&
               math.abs(a.y - b.y) <= PositionEpsilon &&
               math.abs(a.z - b.z) <= PositionEpsilon;
    }

    private static int CompareLex(float3 a, float3 b)
    {
        if (a.x < b.x) return -1;
        if (a.x > b.x) return 1;
        if (a.y < b.y) return -1;
        if (a.y > b.y) return 1;
        if (a.z < b.z) return -1;
        if (a.z > b.z) return 1;
        return 0;
    }
}
