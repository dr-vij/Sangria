using UTools.SourceGeneratorAttributes;

namespace TestNamespace
{
    [ShaderPropertiesProvider]
    public static partial class ShaderProviderExample
    {
        [ShaderName] private const string ShaderName = "ShaderName";
        [ShaderProperty] private const string ShaderProperty = "ShaderPropertyName";
    }
}
