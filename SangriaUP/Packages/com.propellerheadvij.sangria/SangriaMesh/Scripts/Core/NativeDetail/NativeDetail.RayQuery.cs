using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    public partial struct NativeDetail
    {
        public bool RayHitsPrimitive(int primitiveIndex, float3 rayOrigin, float3 rayDir, float tMax,
            ref NativeList<float3> outPositions, ref NativeList<int> outIndices)
        {
            NativeSlice<int> vertices = GetPrimitiveVertices(primitiveIndex);
            int vertCount = vertices.Length;
            if (vertCount < 3)
                return false;

            if (vertCount == 3)
            {
                float3 v0 = GetPointPosition(GetVertexPoint(vertices[0]));
                float3 v1 = GetPointPosition(GetVertexPoint(vertices[1]));
                float3 v2 = GetPointPosition(GetVertexPoint(vertices[2]));

                return SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(
                    rayOrigin, rayDir, 0f, tMax, v0, v1, v2, out _, false);
            }

            var positions = new NativeArray<float3>(vertCount, Allocator.Temp);
            var contourOffsets = new NativeArray<int>(new[] { 0, vertCount }, Allocator.Temp);
            var contourPointIndices = new NativeArray<int>(vertCount, Allocator.Temp);
            try
            {
                for (int i = 0; i < vertCount; i++)
                {
                    positions[i] = GetPointPosition(GetVertexPoint(vertices[i]));
                    contourPointIndices[i] = i;
                }

                var contours = new NativeContourSet(positions, contourOffsets, contourPointIndices);
                outPositions.Clear();
                outIndices.Clear();
                CoreResult result = Triangulation.TriangulateRaw(in contours, ref outPositions, ref outIndices);
                if (result != CoreResult.Success)
                    return false;

                int triCount = outIndices.Length / 3;
                for (int t = 0; t < triCount; t++)
                {
                    float3 v0 = outPositions[outIndices[t * 3]];
                    float3 v1 = outPositions[outIndices[t * 3 + 1]];
                    float3 v2 = outPositions[outIndices[t * 3 + 2]];

                    if (SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(
                            rayOrigin, rayDir, 0f, tMax, v0, v1, v2, out _, false))
                        return true;
                }
            }
            finally
            {
                contourPointIndices.Dispose();
                contourOffsets.Dispose();
                positions.Dispose();
            }

            return false;
        }
    }
}
