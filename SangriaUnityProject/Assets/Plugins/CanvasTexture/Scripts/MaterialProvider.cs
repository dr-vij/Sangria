using System;
using UTools.SourceGeneratorAttributes;

namespace ViJApps.CanvasTexture
{
    [ShaderPropertiesProvider]
    public static partial class MaterialProvider
    {
        //Shader Names
        [ShaderName] private const string SIMPLE_UNLIT = "Shaders/ViJApps.SimpleUnlit";
        [ShaderName] private const string SIMPLE_UNLIT_TRANSPARENT = "Shaders/ViJApps.SimpleUnlitTransparent";
        [ShaderName] private const string SIMPLE_LINE_UNLIT = "Shaders/ViJApps.SimpleLineUnlit";
        [ShaderName] private const string SIMPLE_CIRCLE_UNLIT = "Shaders/ViJApps.SimpleCircleUnlit";
        [ShaderName] private const string SIMPLE_ELLIPSE_UNLIT = "Shaders/ViJApps.SimpleEllipseUnlit";

        //Property 
        [ShaderProperty] private const string COLOR = "_Color";
        [ShaderProperty] private const string FILL_COLOR = "_FillColor";
        [ShaderProperty] private const string STROKE_COLOR = "_StrokeColor";
        
        [ShaderProperty] private const string THICKNESS = "_Thickness";
        [ShaderProperty] private const string FROM_TO_COORD = "_FromToCoord";
        [ShaderProperty] private const string ASPECT = "_Aspect";
        [ShaderProperty] private const string RADIUS = "_Radius";
        [ShaderProperty] private const string CENTER = "_Center";
        [ShaderProperty] private const string AB_FILL_STROKE = "_AbFillStroke";
        
        [ShaderProperty] private const string TRANSFORM_MATRIX = "_TransformMatrix";
        
        [ShaderProperty] private const string MATRIX_COLUMN_0 = "_MatrixColumn0";
        [ShaderProperty] private const string MATRIX_COLUMN_1 = "_MatrixColumn1";
        [ShaderProperty] private const string MATRIX_COLUMN_2 = "_MatrixColumn2";
        [ShaderProperty] private const string MATRIX_COLUMN_3 = "_MatrixColumn3";
    }
}