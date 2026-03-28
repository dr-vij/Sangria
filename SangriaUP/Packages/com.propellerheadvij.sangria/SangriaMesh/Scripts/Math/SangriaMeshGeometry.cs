using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;

namespace SangriaMesh
{
    /// <summary>
    /// Содержит геометрические алгоритмы для SangriaMesh, независимые от Unity Mesh API.
    /// </summary>
    public static class SangriaMeshGeometry
    {
        public const float EarClipEpsilon = 1e-6f;

        /// <summary>
        /// Проецирует 3D полигон на 2D плоскость, отбрасывая ось с наименьшей вариацией нормали.
        /// </summary>
        public static bool TryBuildProjectedPolygon(NativeList<float3> positions, NativeList<float2> projectedPolygon)
        {
            projectedPolygon.Clear();
            int count = positions.Length;
            if (count < 3)
                return false;

            // Newell's method for normal calculation
            float3 normal = default;
            for (int i = 0; i < count; i++)
            {
                float3 current = positions[i];
                float3 next = positions[(i + 1) % count];

                normal.x += (current.y - next.y) * (current.z + next.z);
                normal.y += (current.z - next.z) * (current.x + next.x);
                normal.z += (current.x - next.x) * (current.y + next.y);
            }

            float3 absNormal = math.abs(normal);
            if (math.all(absNormal <= EarClipEpsilon))
                return false;

            int dropAxis;
            if (absNormal.x >= absNormal.y && absNormal.x >= absNormal.z)
                dropAxis = 0;
            else if (absNormal.y >= absNormal.x && absNormal.y >= absNormal.z)
                dropAxis = 1;
            else
                dropAxis = 2;

            for (int i = 0; i < count; i++)
            {
                float3 p = positions[i];
                if (dropAxis == 0)
                    projectedPolygon.Add(p.yz);
                else if (dropAxis == 1)
                    projectedPolygon.Add(p.xz);
                else
                    projectedPolygon.Add(p.xy);
            }

            return true;
        }

        /// <summary>
        /// Выполняет триангуляцию полигона методом Ear Clipping.
        /// </summary>
        public static bool TryTriangulateEarClip(
            NativeList<int> primitiveVertices,
            NativeList<float2> projectedPolygon,
            NativeList<int> polygonOrder,
            NativeArray<int> triangles,
            ref int triangleWriteIndex)
        {
            polygonOrder.Clear();
            int vertexCount = primitiveVertices.Length;
            for (int i = 0; i < vertexCount; i++)
                polygonOrder.Add(i);

            float area2 = ComputeSignedArea2(projectedPolygon, polygonOrder);
            if (math.abs(area2) <= EarClipEpsilon)
                return false;

            bool isCcw = area2 > 0f;
            int guard = 0;
            int guardLimit = vertexCount * vertexCount;

            while (polygonOrder.Length > 3 && guard++ < guardLimit)
            {
                bool earFound = false;
                int count = polygonOrder.Length;

                for (int i = 0; i < count; i++)
                {
                    int prevOrder = (i - 1 + count) % count;
                    int nextOrder = (i + 1) % count;

                    int prev = polygonOrder[prevOrder];
                    int curr = polygonOrder[i];
                    int next = polygonOrder[nextOrder];

                    float2 a = projectedPolygon[prev];
                    float2 b = projectedPolygon[curr];
                    float2 c = projectedPolygon[next];

                    float cross = Cross2(b - a, c - b);
                    if (isCcw ? cross <= EarClipEpsilon : cross >= -EarClipEpsilon)
                        continue;

                    bool containsOtherPoint = false;
                    for (int j = 0; j < count; j++)
                    {
                        if (j == prevOrder || j == i || j == nextOrder)
                            continue;

                        int test = polygonOrder[j];
                        if (PointInTriangleInclusive(projectedPolygon[test], a, b, c))
                        {
                            containsOtherPoint = true;
                            break;
                        }
                    }

                    if (containsOtherPoint)
                        continue;

                    triangles[triangleWriteIndex++] = primitiveVertices[prev];
                    triangles[triangleWriteIndex++] = primitiveVertices[curr];
                    triangles[triangleWriteIndex++] = primitiveVertices[next];

                    polygonOrder.RemoveAt(i);
                    earFound = true;
                    break;
                }

                if (!earFound)
                    return false;
            }

            if (polygonOrder.Length != 3)
                return false;

            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[0]];
            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[1]];
            triangles[triangleWriteIndex++] = primitiveVertices[polygonOrder[2]];
            return true;
        }

        /// <summary>
        /// Записывает триангуляцию веером (fan) для выпуклых полигонов или в качестве запасного варианта.
        /// </summary>
        public static int WriteFanTriangulation(NativeList<int> primitiveVertices, NativeArray<int> triangles, int triangleWriteIndex)
        {
            if (primitiveVertices.Length < 3)
                return triangleWriteIndex;

            int root = primitiveVertices[0];
            for (int i = 1; i < primitiveVertices.Length - 1; i++)
            {
                triangles[triangleWriteIndex++] = root;
                triangles[triangleWriteIndex++] = primitiveVertices[i];
                triangles[triangleWriteIndex++] = primitiveVertices[i + 1];
            }

            return triangleWriteIndex;
        }

        [Conditional("UNITY_ASSERTIONS")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void ValidatePrimitiveTriangulationWrite(
            int primitiveVertexCount,
            int primitiveStartWriteIndex,
            int primitiveEndWriteIndex)
        {
            int expectedIndices = (primitiveVertexCount - 2) * 3;
            int writtenIndices = primitiveEndWriteIndex - primitiveStartWriteIndex;
            if (writtenIndices != expectedIndices)
            {
                throw new InvalidOperationException(
                    $"Invalid triangulation write count. Expected {expectedIndices}, wrote {writtenIndices}.");
            }
        }

        public static float ComputeSignedArea2(NativeList<float2> points, NativeList<int> order)
        {
            float sum = 0f;
            int count = order.Length;

            for (int i = 0; i < count; i++)
            {
                float2 a = points[order[i]];
                float2 b = points[order[(i + 1) % count]];
                sum += a.x * b.y - b.x * a.y;
            }

            return sum;
        }

        public static bool PointInTriangleInclusive(float2 p, float2 a, float2 b, float2 c)
        {
            float c0 = Cross2(b - a, p - a);
            float c1 = Cross2(c - b, p - b);
            float c2 = Cross2(a - c, p - c);

            bool hasPositive = c0 > EarClipEpsilon || c1 > EarClipEpsilon || c2 > EarClipEpsilon;
            bool hasNegative = c0 < -EarClipEpsilon || c1 < -EarClipEpsilon || c2 < -EarClipEpsilon;

            return !(hasPositive && hasNegative);
        }

        public static float Cross2(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }
    }
}
