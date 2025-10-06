using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using ViJApps.CanvasTexture.Utils;

namespace ViJApps.CanvasTexture
{
    public partial class CanvasTexture : IDisposable
    {
        /// <summary>
        /// Draw circle in pixel coordinates
        /// </summary>
        /// <param name="pixelsCenter"></param>
        /// <param name="pixelsRadius"></param>
        /// <param name="color"></param>
        public void DrawCirclePixels(float2 pixelsCenter, float pixelsRadius, Color color)
        {
            var texCenter = pixelsCenter.TransformPoint(m_textureCoordSystem.WorldToZeroOne2d);
            var texRadius = pixelsRadius * m_textureCoordSystem.Height;

            DrawCirclePercent(texCenter, texRadius, color);
        }

        /// <summary>
        /// Draws circle in percent coordinates
        /// </summary>
        /// <param name="center"></param>
        /// <param name="radius"></param>
        /// <param name="color"></param>
        public void DrawCirclePercent(float2 center, float radius, Color color)
        {
            var (mesh, propertyBlock) = AllocateMeshAndPropertyBlock();

            propertyBlock.SetVector(MaterialProvider.CenterPropertyId, new Vector2(center.x, center.y));
            propertyBlock.SetFloat(MaterialProvider.RadiusPropertyId, radius);
            propertyBlock.SetColor(MaterialProvider.ColorPropertyId, color);

            propertyBlock.SetFloat(MaterialProvider.AspectPropertyId, AspectSettings.Aspect);

            var circleMesh = MeshTools.CreateRect(center, new float2(radius, radius), AspectSettings.AspectMatrix2d, mesh);
            var lineMaterial = MaterialProvider.SimpleCircleUnlitShaderId.GetMaterialByShader();

            m_cmd.DrawMesh(circleMesh, Matrix4x4.identity, lineMaterial, 0, -1, propertyBlock);
        }

        /// <summary>
        /// Draws ellipse in pixel coordinates.
        /// </summary>
        /// <param name="pixelsCenter"></param>
        /// <param name="radiusAbPixels"></param>
        /// <param name="color"></param>
        public void DrawEllipsePixels(float2 pixelsCenter, float2 radiusAbPixels, Color color)
        {
            //Convert to texture coordinates
            var center = pixelsCenter.TransformPoint(m_textureCoordSystem.WorldToZeroOne2d);
            var ab = radiusAbPixels.TransformDirection(m_textureCoordSystem.WorldToZeroOne2d);

            //Draw in texture coordinates
            DrawEllipsePercent(center, ab, 0f, color, color);
        }

        /// <summary>
        /// Draw ellipse in percent coordinates
        /// </summary>
        /// <param name="center"></param>
        /// <param name="radiusAb"></param>
        /// <param name="strokeWidth"></param>
        /// <param name="fillColor"></param>
        /// <param name="strokeColor"></param>
        /// <param name="strokeOffset"></param>
        public void DrawEllipsePercent(float2 center, float2 radiusAb, float strokeWidth, Color fillColor, Color strokeColor, float strokeOffset = 0.5f)
        {
            var (mesh, propertyBlock) = AllocateMeshAndPropertyBlock();
            var transform = AspectSettings.AspectMatrix2d;

            //We create rect for this ellipse, so we use 2x radius and 2x strokeWidth
            var (positiveOffset, negativeOffset) = GetOffsets(strokeOffset, strokeWidth);
            var innerPart = math.max(radiusAb + new float2(negativeOffset, negativeOffset), float2.zero);
            var outerPart = math.max(radiusAb + new float2(positiveOffset, positiveOffset), float2.zero);

            mesh = MeshTools.CreateRectTransformed(outerPart * 2, transform, mesh);

            //Prepare property block parameters for ellipse
            propertyBlock.SetColor(MaterialProvider.FillColorPropertyId, fillColor);
            propertyBlock.SetColor(MaterialProvider.StrokeColorPropertyId, strokeColor);
            propertyBlock.SetVector(MaterialProvider.AbFillStrokePropertyId, new Vector4(innerPart.x, innerPart.y, outerPart.x, outerPart.y));

            var inverseTransform = math.inverse(transform);
            propertyBlock.SetMatrix(MaterialProvider.TransformMatrixPropertyId,
                new Matrix4x4(inverseTransform.c0.XYZ0(), inverseTransform.c1.XYZ0(), inverseTransform.c2.XYZ0(), new float4(0, 0, 0, 1)));

            //Ellipse material
            var material = MaterialProvider.SimpleEllipseUnlitShaderId.GetMaterialByShader();

            //Draw with offset by center
            m_cmd.DrawMesh(mesh, MathUtils.CreateMatrix3d_T(new float3(center, 0)), material, 0, -1, propertyBlock);
        }

        /// <summary>
        /// Draws rect in pixel coordinates. TODO: implement stroke
        /// </summary>
        /// <param name="pixelsCenter"></param>
        /// <param name="pixelsSize"></param>
        /// <param name="color"></param>
        public void DrawRectPixels(float2 pixelsCenter, float2 pixelsSize, Color color)
        {
            var center = pixelsCenter.TransformPoint(m_textureCoordSystem.WorldToZeroOne2d);
            var size = pixelsSize.TransformDirection(m_textureCoordSystem.WorldToZeroOne2d);

            DrawRectPercent(center, size, color);
        }

        /// <summary>
        /// Draws rect in percent coordinates. TODO: implement stroke
        /// </summary>
        /// <param name="center"></param>
        /// <param name="size"></param>
        /// <param name="color"></param>
        public void DrawRectPercent(float2 center, float2 size, Color color)
        {
            var (mesh, propertyBlock) = AllocateMeshAndPropertyBlock();

            var rectMesh = MeshTools.CreateRect(center, size, AspectSettings.AspectMatrix2d, mesh);
            propertyBlock.SetColor(MaterialProvider.ColorPropertyId, color);

            var lineMaterial = MaterialProvider.SimpleUnlitShaderId.GetMaterialByShader();
            m_cmd.DrawMesh(rectMesh, Matrix4x4.identity, lineMaterial, 0, -1, propertyBlock);
        }

        /// <summary>
        /// Draws rect in percent coordinates with strokes
        /// </summary>
        /// <param name="center"></param>
        /// <param name="size"></param>
        /// <param name="strokeWidth"></param>
        /// <param name="fillColor"></param>
        /// <param name="strokeColor"></param>
        /// <param name="joinType"></param>
        /// <param name="strokeOffset"></param>
        /// <param name="miterLimit"></param>
        public void DrawRectPercent(float2 center, float2 size, float strokeWidth, Color fillColor, Color strokeColor, LineJoinType joinType = LineJoinType.Miter, float strokeOffset = 0.5f,
            float miterLimit = 1f)
        {
            var (fillMesh, fillPropertyBlock) = AllocateMeshAndPropertyBlock();
            var (strokeMesh, strokePropertyBlock) = AllocateMeshAndPropertyBlock();

            var halfSize = size * 0.5f;
            var points = new List<float2>()
            {
                center + new float2(-halfSize.x, -halfSize.y),
                center + new float2(-halfSize.x, halfSize.y),
                center + new float2(halfSize.x, halfSize.y),
                center + new float2(halfSize.x, -halfSize.y)
            };

            var pointsTransformed = points.TransformPoints(AspectSettings.InverseAspectMatrix2d);
            MeshTools.CreatePolygon(pointsTransformed, strokeWidth, strokeOffset, joinType, miterLimit, fillMesh, strokeMesh);

            var fillMaterial = MaterialProvider.SimpleUnlitTransparentShaderId.GetMaterialByShader();
            var strokeMaterial = MaterialProvider.SimpleUnlitTransparentShaderId.GetMaterialByShader();

            fillPropertyBlock.SetColor(MaterialProvider.ColorPropertyId, fillColor);
            strokePropertyBlock.SetColor(MaterialProvider.ColorPropertyId, strokeColor);

            m_cmd.DrawMesh(fillMesh, AspectSettings.AspectMatrix3d, fillMaterial, 0, 0, fillPropertyBlock);
            m_cmd.DrawMesh(strokeMesh, AspectSettings.AspectMatrix3d, strokeMaterial, 0, 0, strokePropertyBlock);
        }
    }
}