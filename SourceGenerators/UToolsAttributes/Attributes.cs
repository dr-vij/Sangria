using System;

namespace UTools.SourceGeneratorAttributes
{
    public enum Visibility
    {
        Public = 0,
        Internal = 1,
        Protected = 2,
        Private = 3,
    }

    #region Subscription Source Generator attributes

    /// <summary>
    /// TODO: implement optional parameters and property name
    /// This attribute is used for SourceGenerator. Add it to a field to make IDisposable subscription for it
    /// </summary>
    [AttributeUsage(validOn: AttributeTargets.Field)]
    public class PropertySubscription : Attribute
    {
        public Type[] OutputInterfaces { get; set; }

        public Visibility SetterVisibility { get; set; }

        public Visibility GetterVisibility { get; set; }

        public PropertySubscription() { }


        public PropertySubscription(
            Visibility getterVisibility = Visibility.Public,
            Visibility setterVisibility = Visibility.Public,
            params Type[] outputInterfaces)
        {
            GetterVisibility = getterVisibility;
            SetterVisibility = setterVisibility;
            OutputInterfaces = outputInterfaces;
        }


        public PropertySubscription(params Type[] outputInterfaces)
        {
            OutputInterfaces = outputInterfaces;
        }
    }

    #endregion

    #region ShaderProvider Source Generator attributes

    /// <summary>
    /// This attribute is used for SourceGenerator. Add it to partial static class to make it int properties provider
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ShaderPropertiesProviderAttribute : Attribute { }

    /// <summary>
    /// This attribute is used for SourceGenerator. Add it to a string constant with shader name to make a shader property for it
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ShaderNameAttribute : Attribute { }

    /// <summary>
    /// This attribute is used for SourceGenerator. Add it to a string constant with a shader property name to make a shader property for it
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class ShaderPropertyAttribute : Attribute { }

    #endregion

    #region LayerInfo Source Generator attributes

    [AttributeUsage(AttributeTargets.Field)]
    public class ExportLayerInfo : Attribute { }

    #endregion
}