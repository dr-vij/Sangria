using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    public partial struct NativeDetail
    {
        public bool RayHitsPrimitive(int primitiveIndex, float3 rayOrigin, float3 rayDir, float tMax,
            ref NativeList<float3> outPositions, ref NativeList<int> outIndices, bool useConvexFanPath)
        {
            NativeSlice<int> vertices = GetPrimitiveVertices(primitiveIndex);
            int vertCount = vertices.Length;
            if (vertCount < 3)
                return false;

            if (TryGetPointAccessor(AttributeID.Position, out NativeAttributeAccessor<float3> pointPositions) != CoreResult.Success)
                return false;

            NativeArray<int> vertexToPoint = m_VertexToPoint.AsArray();

            if (vertCount == 3)
            {
                float3 v0 = pointPositions.GetRefUnchecked(vertexToPoint[vertices[0]]);
                float3 v1 = pointPositions.GetRefUnchecked(vertexToPoint[vertices[1]]);
                float3 v2 = pointPositions.GetRefUnchecked(vertexToPoint[vertices[2]]);

                return SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(
                    rayOrigin, rayDir, 0f, tMax, v0, v1, v2, out _, false);
            }

            if (outPositions.Capacity < vertCount)
                outPositions.Capacity = vertCount;
            if (outIndices.Capacity < vertCount)
                outIndices.Capacity = vertCount;

            outPositions.Clear();
            outIndices.Clear();

            for (int i = 0; i < vertCount; i++)
            {
                outPositions.Add(pointPositions.GetRefUnchecked(vertexToPoint[vertices[i]]));
                outIndices.Add(i);
            }

            if (useConvexFanPath)
                return TryRayHitConvexPolygonFan(rayOrigin, rayDir, tMax, outPositions.AsArray());

            var contourOffsets = new NativeArray<int>(2, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            try
            {
                contourOffsets[0] = 0;
                contourOffsets[1] = vertCount;

                var contours = new NativeContourSet(outPositions.AsArray(), contourOffsets, outIndices.AsArray());
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
                contourOffsets.Dispose();
            }

            return false;
        }

        private static bool TryRayHitConvexPolygonFan(
            in float3 rayOrigin,
            in float3 rayDir,
            float tMax,
            NativeArray<float3> polygonPositions)
        {
            float3 anchor = polygonPositions[0];
            int vertCount = polygonPositions.Length;
            for (int i = 1; i < vertCount - 1; i++)
            {
                if (SangriaMeshRayTriangleIntersectors.TryIntersectMoeller(
                        rayOrigin, rayDir, 0f, tMax,
                        anchor, polygonPositions[i], polygonPositions[i + 1], out _, false))
                    return true;
            }

            return false;
        }
    }
}
