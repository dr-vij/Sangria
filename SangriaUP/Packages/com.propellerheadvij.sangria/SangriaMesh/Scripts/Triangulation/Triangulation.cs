using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    public enum TriangulationWindingRule : byte
    {
        EvenOdd = 0,
        NonZero = 1,
        Positive = 2,
        Negative = 3,
        AbsGeqTwo = 4
    }

    public enum TriangulationContourOrientation : byte
    {
        Original = 0,
        Clockwise = 1,
        CounterClockwise = 2
    }

    public struct TriangulationOptions
    {
        public TriangulationWindingRule WindingRule;
        public TriangulationContourOrientation ContourOrientation;
        public float3 Normal;
        public bool RemoveEmptyPolygons;

        public static TriangulationOptions Default => new TriangulationOptions
        {
            WindingRule = TriangulationWindingRule.EvenOdd,
            ContourOrientation = TriangulationContourOrientation.Original,
            Normal = float3.zero,
            RemoveEmptyPolygons = false
        };
    }

    /// <summary>
    /// Native contour input for triangulation. Offsets are prefix-sum style and must end with <c>ContourPointIndices.Length</c>.
    /// </summary>
    public readonly struct NativeContourSet
    {
        public readonly NativeArray<float3>.ReadOnly Positions;
        public readonly NativeArray<int>.ReadOnly ContourOffsets;
        public readonly NativeArray<int>.ReadOnly ContourPointIndices;

        public NativeContourSet(
            NativeArray<float3> positions,
            NativeArray<int> contourOffsets,
            NativeArray<int> contourPointIndices)
            : this(positions.AsReadOnly(), contourOffsets.AsReadOnly(), contourPointIndices.AsReadOnly())
        {
        }

        public NativeContourSet(
            NativeArray<float3>.ReadOnly positions,
            NativeArray<int>.ReadOnly contourOffsets,
            NativeArray<int>.ReadOnly contourPointIndices)
        {
            Positions = positions;
            ContourOffsets = contourOffsets;
            ContourPointIndices = contourPointIndices;
        }

        public int ContourCount => math.max(0, ContourOffsets.Length - 1);

        public bool IsCreated => Positions.IsCreated && ContourOffsets.IsCreated && ContourPointIndices.IsCreated;

        public CoreResult Validate()
        {
            if (!IsCreated)
                return CoreResult.InvalidOperation;

            if (ContourOffsets.Length == 0)
                return ContourPointIndices.Length == 0 ? CoreResult.Success : CoreResult.InvalidOperation;

            if (ContourOffsets[0] != 0)
                return CoreResult.InvalidOperation;

            if (ContourOffsets[ContourOffsets.Length - 1] != ContourPointIndices.Length)
                return CoreResult.InvalidOperation;

            for (int contourIndex = 0; contourIndex < ContourCount; contourIndex++)
            {
                int start = ContourOffsets[contourIndex];
                int end = ContourOffsets[contourIndex + 1];

                if (end < start)
                    return CoreResult.InvalidOperation;
                if (end - start < 3)
                    return CoreResult.InvalidOperation;
            }

            for (int i = 0; i < ContourPointIndices.Length; i++)
            {
                int pointIndex = ContourPointIndices[i];
                if ((uint)pointIndex >= (uint)Positions.Length)
                    return CoreResult.IndexOutOfRange;
            }

            return CoreResult.Success;
        }
    }

    public static class Triangulation
    {
        public static CoreResult TriangulateContours(
            in NativeContourSet contours,
            ref NativeDetail output,
            in TriangulationOptions options = default)
        {
            CoreResult validation = contours.Validate();
            if (validation != CoreResult.Success)
                return validation;

            if (output.PointCount != 0 || output.VertexCount != 0 || output.PrimitiveCount != 0)
                return CoreResult.InvalidOperation;

            if (contours.ContourCount == 0)
                return CoreResult.Success;

            return NativeTess.NativeTessAPI.Tessellate(in contours, ref output, in options);
        }

        public static CoreResult TriangulateContours(
            in NativeContourSet contours,
            ref NativeDetail output,
            out ProvenanceMap provenance,
            in TriangulationOptions options = default)
        {
            provenance = default;

            CoreResult validation = contours.Validate();
            if (validation != CoreResult.Success)
                return validation;

            if (output.PointCount != 0 || output.VertexCount != 0 || output.PrimitiveCount != 0)
                return CoreResult.InvalidOperation;

            if (contours.ContourCount == 0)
                return CoreResult.Success;

            return NativeTess.NativeTessAPI.Tessellate(in contours, ref output, out provenance, in options);
        }
    }
}
