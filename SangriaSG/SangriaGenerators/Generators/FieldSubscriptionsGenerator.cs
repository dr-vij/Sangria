using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Sangria.SourceGeneratorAttributes;

namespace Sangria.SourceGenerators
{
    [Generator]
    public class FieldSubscriptionsGenerator : ISourceGenerator
    {
        private readonly List<UsingDirectiveSyntax> m_ExtraUsing = new()
        {
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Sangria")),
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")),
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Collections.Generic")),
        };

        private readonly Dictionary<string, InterfaceBuilder> m_InterfaceBuilders = new();

        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var compilation = context.Compilation;
            var subscriptionAttribute = nameof(PropertySubscription);

            var fieldAttributes = new[] { subscriptionAttribute };

            var classNodes = compilation.GetClassesByFieldAttributes(fieldAttributes);
            var counter = 0;

            foreach (var classNode in classNodes)
            {
                m_InterfaceBuilders.Clear();

                var className = classNode.Identifier.Text;
                var fieldNodes = classNode.Members
                    .OfType<FieldDeclarationSyntax>()
                    .Where(fieldNode => fieldAttributes.Any(fieldNode.HasAttribute));

                var newClass = SyntaxFactory.ClassDeclaration(
                    default,
                    classNode.Modifiers,
                    classNode.Identifier,
                    classNode.TypeParameterList,
                    null,
                    classNode.ConstraintClauses,
                    SyntaxFactory.List<MemberDeclarationSyntax>());

                foreach (var fieldNode in fieldNodes)
                {
                    var isStatic = fieldNode.Modifiers.Any(SyntaxKind.StaticKeyword);
                    var fieldType = fieldNode.Declaration.Type;

                    var fieldName = fieldNode.Declaration.Variables.First().Identifier.Text;
                    var privateEventName = $"{fieldName}Changed";
                    var propertyName = fieldName.RemovePrefix();
                    var subscriptionMethodName = $"SubscribeTo{propertyName}";
                    var partialMethodName = $"On{propertyName}Change";
                    var partialBeforeMethodName = $"Before{propertyName}Change";

                    // Prepare getter and setter visibility using helper
                    var setterVisibility = GetVisibility(fieldNode, compilation, subscriptionAttribute, nameof(PropertySubscription.SetterVisibility), Visibility.Public);
                    var getterVisibility = GetVisibility(fieldNode, compilation, subscriptionAttribute, nameof(PropertySubscription.GetterVisibility), Visibility.Public);

                    var eventField = CreateEventField(fieldType, privateEventName, isStatic);

                    var propertyDeclaration = CreatePropertyAndCallbacks(
                        fieldType,
                        propertyName,
                        fieldName,
                        partialBeforeMethodName,
                        partialMethodName,
                        privateEventName,
                        isStatic,
                        getterVisibility,
                        setterVisibility
                    );

                    var partialBeforeMethod = CreatePartialMethod(partialBeforeMethodName, fieldType, isStatic, true);
                    var partialMethod = CreatePartialMethod(partialMethodName, fieldType, isStatic, false);

                    var members = new List<MemberDeclarationSyntax>
                    {
                        partialBeforeMethod,
                        partialMethod,
                        eventField,
                        propertyDeclaration
                    };

                    var hasDisposableSubscription = fieldNode.HasAttribute(subscriptionAttribute);

                    if (hasDisposableSubscription)
                    {
                        //We try to find interface from attribute, and property visibility
                        compilation.TryGetTypeFromAttributeInterfaceProperty(fieldNode, subscriptionAttribute, out var interfaceTypes);

                        //Prepare the interfaces and their subscriptions (method-based)
                        foreach (var interfaceType in interfaceTypes)
                        {
                            var key = interfaceType.ContainingNamespace + interfaceType.Name;
                            if (!m_InterfaceBuilders.TryGetValue(key, out var interfaceBuilder))
                            {
                                interfaceBuilder = new InterfaceBuilder(key, interfaceType);
                                m_InterfaceBuilders.Add(key, interfaceBuilder);
                            }

                            interfaceBuilder.AddInterfaceProperty(fieldType, propertyName, getterVisibility, setterVisibility);
                            interfaceBuilder.AddInterfaceSubscriptionMethod(fieldType, subscriptionMethodName, getterVisibility);
                        }

                        var subscription = CreateDisposableSubscriptionMethod(fieldType, subscriptionMethodName, fieldName, privateEventName, isStatic, getterVisibility);
                        members.Add(subscription);
                    }

                    newClass = newClass.AddMembers(members.ToArray());
                }

                var combinedUsing = classNode
                    .GetUsingArr()
                    .Concat(m_ExtraUsing)
                    .MakeDistinct()
                    .ToArray();

                var compilationUnit = SyntaxFactory.CompilationUnit()
                    .AddUsings(combinedUsing);

                var interfaces = m_InterfaceBuilders.Values.Select(builder => builder.ToSyntaxNode()).ToArray();
                foreach (var interfaceBuilder in m_InterfaceBuilders.Values)
                    newClass = newClass.AddBaseListTypes(interfaceBuilder.ToSimpleBaseTypeSyntax());

                var classWithHierarchy = classNode.CopyHierarchyTo(newClass);
                compilationUnit = compilationUnit
                    .AddMembers(interfaces)
                    .AddMembers(classWithHierarchy);

                var code = compilationUnit
                    .NormalizeWhitespace()
                    .ToFullString();
                context.AddSource(className + $"Gen{counter++}.cs", SourceText.From(code, Encoding.UTF8));
            }
        }

        private static string ToVisibilityKeyword(Visibility visibility)
        {
            switch (visibility)
            {
                case Visibility.Public: return "public";
                case Visibility.Internal: return "internal";
                case Visibility.Protected: return "protected";
                case Visibility.Private: return "private";
                default: return "public";
            }
        }

        private static MethodDeclarationSyntax CreateDisposableSubscriptionMethod(
            TypeSyntax fieldType,
            string subscriptionMethodName,
            string fieldName,
            string eventName,
            bool isStatic,
            Visibility visibility
        )
        {
            // Hybrid approach: build readable C# as a string and parse into a syntax node
            var vis = ToVisibilityKeyword(visibility);
            var staticKw = isStatic ? " static" : string.Empty;
            var typeText = fieldType.ToString();
            var handlerType = isStatic ? $"Action<{typeText}>" : $"EventHandler<{typeText}>";
            var invokeArgs = isStatic ? fieldName : $"this, {fieldName}";

            var code = $@"{vis}{staticKw} IDisposable {subscriptionMethodName}({handlerType} handler, bool initialCall = true)
            {{
                {eventName} += handler;
                if (initialCall) handler?.Invoke({invokeArgs});
                return new DisposeAction(() => {eventName} -= handler);
            }}";

            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(code);
        }

        private static Visibility GetVisibility(FieldDeclarationSyntax fieldNode, Compilation compilation, string attributeName, string parameterName, Visibility defaultValue)
        {
            var found = fieldNode.TryGetAttributeParameter(
                compilation,
                attributeName,
                parameterName,
                out var expr);
            if (!found || expr == null)
                return defaultValue;

            var lastWord = expr.ToString().GetLastWord();
            try
            {
                return (Visibility)Enum.Parse(typeof(Visibility), lastWord);
            }
            catch
            {
                return defaultValue;
            }
        }


        /// <summary>
        /// Create the event field declaration
        /// </summary>
        /// <param name="fieldType">the event parameters type</param>
        /// <param name="eventName">name of generated field</param>
        /// <param name="isStatic">makes event declaration static when true</param>
        /// <returns></returns>
        private static EventFieldDeclarationSyntax CreateEventField(TypeSyntax fieldType, string eventName,
            bool isStatic = false)
        {
            var typeText = fieldType.ToString();
            var handlerType = isStatic ? $"Action<{typeText}>" : $"EventHandler<{typeText}>";
            var staticKw = isStatic ? " static" : string.Empty;
            var code = $"private{staticKw} event {handlerType} {eventName};";
            return (EventFieldDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(code);
        }

        /// <summary>
        /// Create the partial void method with given parameter
        /// </summary>
        /// <param name="methodName">the name of generated method</param>
        /// <param name="parameterType">type of generated method parameter</param>
        /// <param name="isStatic">makes method declaration static when true</param>
        /// <param name="isRefInput">adds ref keyword to the callback parameter</param>
        /// <returns></returns>
        private static MethodDeclarationSyntax CreatePartialMethod(string methodName, TypeSyntax parameterType,
            bool isStatic, bool isRefInput)
        {
            var staticKw = isStatic ? "static " : string.Empty;
            var refKw = isRefInput ? "ref " : string.Empty;
            var typeText = parameterType.ToString();
            var code = $"{staticKw}partial void {methodName}({refKw}{typeText} newValue);";
            return (MethodDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(code);
        }

        /// <summary>
        /// Create the property declaration with given field name and type.
        /// the property checks the field changes, and invokes OnChanged event and partial class
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="fieldName"></param>
        /// <param name="methodCallbackName"></param>
        /// <param name="eventCallbackName"></param>
        /// <param name="fieldType"></param>
        /// <param name="isStatic"></param>
        /// <param name="getterVisibility"></param>
        /// <param name="setterVisibility"></param>
        /// <returns></returns>
        private PropertyDeclarationSyntax CreatePropertyAndCallbacks(
            TypeSyntax fieldType,
            string propertyName,
            string fieldName,
            string partialBeforeMethodName,
            string methodCallbackName,
            string eventCallbackName,
            bool isStatic,
            Visibility getterVisibility,
            Visibility setterVisibility
        )
        {
            var lessRestrictiveVisibility = getterVisibility < setterVisibility ? getterVisibility : setterVisibility;
            var needGetterRestriction = getterVisibility > setterVisibility;
            var needSetterRestriction = setterVisibility > getterVisibility;

            var propVis = ToVisibilityKeyword(lessRestrictiveVisibility);
            var staticKw = isStatic ? " static" : string.Empty;
            var typeText = fieldType.ToString();

            var eventInvoke = isStatic
                ? $"{eventCallbackName}?.Invoke(value);"
                : $"{eventCallbackName}?.Invoke(this, value);";

            var getterMod = needGetterRestriction ? ToVisibilityKeyword(getterVisibility) + " " : string.Empty;
            var setterMod = needSetterRestriction ? ToVisibilityKeyword(setterVisibility) + " " : string.Empty;

            var code = $@"{propVis}{staticKw} {typeText} {propertyName}
{{
    {getterMod}get {{ return {fieldName}; }}
    {setterMod}set
    {{
        {partialBeforeMethodName}(ref value);
        if (!EqualityComparer<{typeText}>.Default.Equals({fieldName}, value))
        {{
            {fieldName} = value;
            {methodCallbackName}(value);
            {eventInvoke}
        }}
    }}
}}";

            return (PropertyDeclarationSyntax)SyntaxFactory.ParseMemberDeclaration(code);
        }
    }
}
