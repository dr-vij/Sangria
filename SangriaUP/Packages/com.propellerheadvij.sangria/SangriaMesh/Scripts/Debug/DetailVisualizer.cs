using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SangriaMesh
{
    public static class DetailVisualizer
    {
        public static void DrawPointGizmos(this ref NativeDetail detail, float pointSize, Color pointColor)
        {
            if (detail.TryGetPointAccessor<float3>(AttributeID.Position, out var positionAccessor) != CoreResult.Success)
                return;

            using var alivePoints = new NativeList<int>(Allocator.Temp);
            detail.GetAllValidPoints(alivePoints);

            var initialColor = Gizmos.color;
            Gizmos.color = pointColor;

            for (int i = 0; i < alivePoints.Length; i++)
            {
                int pointIndex = alivePoints[i];
                float3 pos = positionAccessor[pointIndex];
                Gizmos.DrawWireCube(pos, new float3(pointSize, pointSize, pointSize));
            }

            Gizmos.color = initialColor;
        }

        public static void DrawVertexNormalsGizmos(this ref NativeDetail detail, float normalLength, Color normalColor)
        {
            if (detail.TryGetPointAccessor<float3>(AttributeID.Position, out var positionAccessor) != CoreResult.Success)
                return;

            bool hasVertexNormals =
                detail.TryGetVertexAccessor<float3>(AttributeID.Normal, out var vertexNormalAccessor) == CoreResult.Success;
            bool hasPointNormals =
                detail.TryGetPointAccessor<float3>(AttributeID.Normal, out var pointNormalAccessor) == CoreResult.Success;

            if (!hasVertexNormals && !hasPointNormals)
                return;

            using var aliveVertices = new NativeList<int>(Allocator.Temp);
            detail.GetAllValidVertices(aliveVertices);

            var initialColor = Gizmos.color;
            Gizmos.color = normalColor;

            for (int i = 0; i < aliveVertices.Length; i++)
            {
                int vertexIndex = aliveVertices[i];
                int pointIndex = detail.GetVertexPoint(vertexIndex);
                if (pointIndex < 0)
                    continue;

                float3 pos = positionAccessor[pointIndex];
                float3 normal = hasVertexNormals ? vertexNormalAccessor[vertexIndex] : pointNormalAccessor[pointIndex];
                float3 endPoint = pos + normal * normalLength;
                Gizmos.DrawLine(pos, endPoint);
            }

            Gizmos.color = initialColor;
        }

        public static void DrawPointNumbers(this ref NativeDetail detail, Color textColor, float offset = 0.1f)
        {
#if UNITY_EDITOR
            if (detail.TryGetPointAccessor<float3>(AttributeID.Position, out var positionAccessor) != CoreResult.Success)
                return;

            using var alivePoints = new NativeList<int>(Allocator.Temp);
            detail.GetAllValidPoints(alivePoints);

            var style = new GUIStyle
            {
                normal = { textColor = textColor },
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            for (int i = 0; i < alivePoints.Length; i++)
            {
                int pointIndex = alivePoints[i];
                float3 pos = positionAccessor[pointIndex];
                Vector3 labelPos = pos + new float3(offset, offset, offset);
                Handles.Label(labelPos, pointIndex.ToString(), style);
            }
#endif
        }

        public static void DrawPrimitiveLines(this ref NativeDetail detail, Color lineColor)
        {
            if (detail.TryGetPointAccessor<float3>(AttributeID.Position, out var positionAccessor) != CoreResult.Success)
                return;

            var initialColor = Gizmos.color;
            Gizmos.color = lineColor;

            using var validPrimitives = new NativeList<int>(Allocator.Temp);
            detail.GetAllValidPrimitives(validPrimitives);

            for (int i = 0; i < validPrimitives.Length; i++)
            {
                int primitiveIndex = validPrimitives[i];
                var primVertices = detail.GetPrimitiveVertices(primitiveIndex);
                if (primVertices.Length < 2)
                    continue;

                for (int v = 0; v < primVertices.Length; v++)
                {
                    int currentVertexIndex = primVertices[v];
                    int nextVertexIndex = primVertices[(v + 1) % primVertices.Length];

                    int currentPointIndex = detail.GetVertexPoint(currentVertexIndex);
                    int nextPointIndex = detail.GetVertexPoint(nextVertexIndex);
                    if (currentPointIndex < 0 || nextPointIndex < 0)
                        continue;

                    float3 currentPos = positionAccessor[currentPointIndex];
                    float3 nextPos = positionAccessor[nextPointIndex];
                    Gizmos.DrawLine(currentPos, nextPos);
                }
            }

            Gizmos.color = initialColor;
        }
    }
}
