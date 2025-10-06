using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace PropellerheadMesh
{
    public static class DetailVisualizer
    {
        public static void DrawPointGizmos(this NativeDetail detail, float pointSize, Color pointColor)
        {
            detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);
            var initialColor = Gizmos.color;
            Gizmos.color = pointColor;
            for (int i = 0; i < detail.PointCount; i++)
            {
                float3 pos = positionAccessor[i];
                Gizmos.DrawWireCube(pos, new float3(pointSize, pointSize, pointSize));
            }

            Gizmos.color = initialColor;
        }

        public static void DrawVertexNormalsGizmos(this NativeDetail detail, float normalLength, Color normalColor)
        {
            detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);
            detail.GetVertexAttributeAccessor<float3>(AttributeID.Normal, out var normalAccessor);

            var initialColor = Gizmos.color;
            Gizmos.color = normalColor;

            for (int i = 0; i < detail.VertexCount; i++)
            {
                int pointIndex = detail.GetVertexPoint(i);
                float3 pos = positionAccessor[pointIndex];
                float3 normal = normalAccessor[i];
                float3 endPoint = pos + normal * normalLength;

                Gizmos.DrawLine(pos, endPoint);
            }

            Gizmos.color = initialColor;
        }

        public static void DrawPointNumbers(this NativeDetail detail, Color textColor, float offset = 0.1f)
        {
#if UNITY_EDITOR

            detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);

            var style = new GUIStyle
            {
                normal =
                {
                    textColor = textColor
                },
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };

            for (int i = 0; i < detail.PointCount; i++)
            {
                float3 pos = positionAccessor[i];
                Vector3 labelPos = pos + new float3(offset, offset, offset);
                Handles.Label(labelPos, i.ToString(), style);
            }
#endif
        }
        
        public static void DrawPrimitiveLines(this NativeDetail detail, Color lineColor)
        {
            detail.GetPointAttributeAccessor<float3>(AttributeID.Position, out var positionAccessor);

            var initialColor = Gizmos.color;
            Gizmos.color = lineColor;

            // Get all valid primitives
            var validPrimitives = new NativeList<int>(Allocator.Temp);
            detail.GetAllValidPrimitives(validPrimitives);

            for (int i = 0; i < validPrimitives.Length; i++)
            {
                int primIndex = validPrimitives[i];
                
                // Get primitive vertex indices
                var primVertices = detail.GetPrimitiveVertices(primIndex);
                if (primVertices.Length < 2)
                    continue;

                // Draw lines connecting consecutive vertices
                for (int v = 0; v < primVertices.Length; v++)
                {
                    int currentVertexIndex = primVertices[v];
                    int nextVertexIndex = primVertices[(v + 1) % primVertices.Length]; // Wrap around to first vertex
                    
                    int currentPointIndex = detail.GetVertexPoint(currentVertexIndex);
                    int nextPointIndex = detail.GetVertexPoint(nextVertexIndex);
                    
                    if (currentPointIndex >= 0 && nextPointIndex >= 0)
                    {
                        float3 currentPos = positionAccessor[currentPointIndex];
                        float3 nextPos = positionAccessor[nextPointIndex];
                        
                        Gizmos.DrawLine(currentPos, nextPos);
                    }
                }
            }

            validPrimitives.Dispose();
            Gizmos.color = initialColor;
        }
    }
}