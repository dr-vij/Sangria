using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using UTools.SourceGeneratorAttributes;

namespace UTools.SourceGenerators
{
    [Generator]
    public class FieldSubscriptionsGenerator : ISourceGenerator
    {
        private readonly List<MemberDeclarationSyntax> m_Members = new();

        private readonly List<UsingDirectiveSyntax> m_ExtraUsing = new()
        {
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("UTools")),
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
                    var subscriptionEventName = $"{propertyName}Changed";
                    var subscriptionMethodName = $"SubscribeTo{propertyName}";
                    var partialMethodName = $"On{propertyName}Change";
                    var partialBeforeMethodName = $"Before{propertyName}Change";

                    //PREPARE GETTER AND SETTERS VISIBILITY
                    var setterVisibilityFound = fieldNode.TryGetAttributeParameter(
                        compilation,
                        subscriptionAttribute,
                        nameof(PropertySubscription.SetterVisibility),
                        out var setterVisibilityExp);
                    var getterVisibilityFound = fieldNode.TryGetAttributeParameter(
                        compilation, subscriptionAttribute,
                        nameof(PropertySubscription.GetterVisibility),
                        out var getterVisibilityExp);

                    var setterVisibility = Visibility.Public;
                    if (setterVisibilityFound)
                        setterVisibility = (Visibility)Enum.Parse(typeof(Visibility), setterVisibilityExp.ToString().GetLastWord());

                    var getterVisibility = Visibility.Public;
                    if (getterVisibilityFound)
                        getterVisibility = (Visibility)Enum.Parse(typeof(Visibility), getterVisibilityExp.ToString().GetLastWord());

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

                    m_Members.Clear();
                    m_Members.Add(partialBeforeMethod);
                    m_Members.Add(partialMethod);
                    m_Members.Add(eventField);
                    m_Members.Add(propertyDeclaration);

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
                        m_Members.Add(subscription);
                    }

                    newClass = newClass.AddMembers(m_Members.ToArray());
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

        private static MethodDeclarationSyntax CreateDisposableSubscriptionMethod(
            TypeSyntax fieldType,
            string subscriptionMethodName,
            string fieldName,
            string eventName,
            bool isStatic,
            Visibility visibility
        )
        {
            var visibilityToken = visibility.ToVisibilitySyntaxKind();

            var modifiers = isStatic
                ? SyntaxFactory.TokenList(SyntaxFactory.Token(visibilityToken),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                : SyntaxFactory.TokenList(SyntaxFactory.Token(visibilityToken));

            var eventHandlerTypeName = isStatic ? "Action" : "EventHandler";
            var eventHandlerTypeArgumentList =
                SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(fieldType));
            var eventHandlerType = SyntaxFactory.GenericName(SyntaxFactory.Identifier(eventHandlerTypeName))
                .WithTypeArgumentList(eventHandlerTypeArgumentList);

            // Create first parameter - handler
            var handlerParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("handler"))
                .WithType(eventHandlerType);

            // Create second parameter - initialCall with default value true
            var initialCallParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("initialCall"))
                .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)))
                .WithDefault(SyntaxFactory.EqualsValueClause(
                    SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));

            // Create parameter list with both parameters
            var parameters = SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList(new[] { handlerParameter, initialCallParameter }));

            var eventNameExpression = SyntaxFactory.IdentifierName(eventName);
            var addAssignment = SyntaxFactory.AssignmentExpression(SyntaxKind.AddAssignmentExpression,
                eventNameExpression, SyntaxFactory.IdentifierName("handler"));
            var removeAssignment =
                SyntaxFactory.AssignmentExpression(SyntaxKind.SubtractAssignmentExpression, eventNameExpression,
                    SyntaxFactory.IdentifierName("handler"));

            var handlerInvokeArguments = isStatic
                ? SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName(fieldName))))
                : SyntaxFactory.ArgumentList(
                    SyntaxFactory.SeparatedList(
                        new[]
                        {
                            SyntaxFactory.Argument(SyntaxFactory.ThisExpression()),
                            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(fieldName))
                        }
                    )
                );

            // Create conditional handler invocation - only if initialCall is true
            var handlerInvokeExpression = SyntaxFactory.ConditionalAccessExpression(
                SyntaxFactory.IdentifierName("handler"),
                SyntaxFactory
                    .InvocationExpression(
                        SyntaxFactory.MemberBindingExpression(SyntaxFactory.IdentifierName("Invoke")))
                    .WithArgumentList(handlerInvokeArguments));

            var conditionalHandlerInvokeStatement = SyntaxFactory.IfStatement(
                SyntaxFactory.IdentifierName("initialCall"),
                SyntaxFactory.ExpressionStatement(handlerInvokeExpression));

            var returnStatement = SyntaxFactory.ReturnStatement(
                SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("DisposeAction"))
                    .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.ParenthesizedLambdaExpression(removeAssignment))))));

            // Create method body with subscription, conditional invocation, and return statement
            var methodBody = SyntaxFactory.Block(
                SyntaxFactory.ExpressionStatement(addAssignment),
                conditionalHandlerInvokeStatement,
                returnStatement);

            var methodDeclaration = SyntaxFactory.MethodDeclaration(SyntaxFactory.IdentifierName("IDisposable"),
                    SyntaxFactory.Identifier(subscriptionMethodName))
                .WithModifiers(modifiers)
                .WithParameterList(parameters)
                .WithBody(methodBody);

            return methodDeclaration;
        }

        /// <summary>
        ///  Create the subscription event for the given event name and field name
        /// </summary>
        /// <param name="fieldType"></param>
        /// <param name="subscriptionEventName"></param>
        /// <param name="fieldName"></param>
        /// <param name="privateEventName"></param>
        /// <param name="isStatic"></param>
        /// <param name="visibility"></param>
        /// <returns></returns>
        private static EventDeclarationSyntax CreateSubscriptionEvent(
            TypeSyntax fieldType,
            string subscriptionEventName,
            string fieldName,
            string privateEventName,
            bool isStatic,
            Visibility visibility
        )
        {
            var genericType = isStatic ? "Action" : "EventHandler";
            var visibilityToken = visibility.ToVisibilitySyntaxKind();
            // public static or public
            var modifiers = isStatic
                ? SyntaxFactory.TokenList(SyntaxFactory.Token(visibilityToken),
                    SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                : SyntaxFactory.TokenList(SyntaxFactory.Token(visibilityToken));

            //Prepare add and remove statements
            var invokeStatement = isStatic
                ? $"value?.Invoke({fieldName});"
                : $"value?.Invoke(this, {fieldName});";
            var addStatements = SyntaxFactory.List(new StatementSyntax[]
            {
                SyntaxFactory.ExpressionStatement(SyntaxFactory.ParseExpression(invokeStatement)),
                SyntaxFactory.ExpressionStatement(SyntaxFactory.ParseExpression($"{privateEventName} += value"))
            });

            var removeStatements = SyntaxFactory.List(new StatementSyntax[]
            {
                SyntaxFactory.ExpressionStatement(SyntaxFactory.ParseExpression($"{privateEventName} -= value"))
            });

            var addAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.AddAccessorDeclaration)
                .WithBody(SyntaxFactory.Block(addStatements));

            var removeAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.RemoveAccessorDeclaration)
                .WithBody(SyntaxFactory.Block(removeStatements));

            //Buildup the event declaration
            var eventSyntax = SyntaxFactory.EventDeclaration(
                    SyntaxFactory.GenericName(SyntaxFactory.Identifier(genericType))
                        .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SingletonSeparatedList(fieldType))),
                    SyntaxFactory.Identifier(subscriptionEventName))
                .WithModifiers(modifiers)
                .WithAccessorList(
                    SyntaxFactory.AccessorList(SyntaxFactory.List(new[] { addAccessor, removeAccessor })));
            return eventSyntax;
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
            var eventHandlerType = isStatic ? $"Action<{fieldType}>" : $"EventHandler<{fieldType}>";

            var modifiers = isStatic
                ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword))
                : SyntaxFactory.TokenList();
            modifiers.Add(SyntaxFactory.Token(SyntaxKind.PrivateKeyword));

            return SyntaxFactory.EventFieldDeclaration(
                    SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(eventHandlerType))
                        .WithVariables(
                            SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(eventName))))
                .WithModifiers(modifiers);
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
            var modifiers = isStatic
                ? SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword),
                    SyntaxFactory.Token(SyntaxKind.PartialKeyword))
                : SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

            var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("newValue"))
                .WithType(parameterType);

            if (isRefInput)
                parameter = parameter.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.RefKeyword)));

            return SyntaxFactory
                .MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                    methodName)
                .WithModifiers(modifiers)
                .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(parameter)))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
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

            var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(lessRestrictiveVisibility.ToVisibilitySyntaxKind()));
            if (isStatic)
                modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.StaticKeyword));

            var eventInvocation = isStatic
                ? $"{eventCallbackName}?.Invoke(value);"
                : $"{eventCallbackName}?.Invoke(this, value)";
            var eventInvocationExpression = SyntaxFactory.ParseExpression(eventInvocation);

            var getAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(
                SyntaxFactory.Block(SyntaxFactory.SingletonList<StatementSyntax>(
                    SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(fieldName)))));
            getAccessor = needGetterRestriction
                ? getAccessor.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(getterVisibility.ToVisibilitySyntaxKind())))
                : getAccessor;

            var notEqualsExpressionCode = $"!EqualityComparer<{fieldType}>.Default.Equals({fieldName}, value)";
            var notEqualsExpression = SyntaxFactory.ParseExpression(notEqualsExpressionCode);

            //partial method invocation before the field assignment, with ref keyword
            var partialBeforeMethodInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(partialBeforeMethodName),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName("value")).WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword)))));

            //partial method invocation after the field assignment
            var partialMethodInvocation = SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName(methodCallbackName),
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(SyntaxFactory.IdentifierName("value")))));

            var assignment = SyntaxFactory.AssignmentExpression(
                SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName(fieldName),
                SyntaxFactory.IdentifierName("value"));

            var setAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration).WithBody(
                SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(partialBeforeMethodInvocation),
                    SyntaxFactory.IfStatement(notEqualsExpression,
                        SyntaxFactory.Block(
                            SyntaxFactory.ExpressionStatement(assignment),
                            SyntaxFactory.ExpressionStatement(partialMethodInvocation),
                            SyntaxFactory.ExpressionStatement(eventInvocationExpression)
                        ))));

            setAccessor = needSetterRestriction
                ? setAccessor.WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(setterVisibility.ToVisibilitySyntaxKind())))
                : setAccessor;

            var propertyDeclaration = SyntaxFactory.PropertyDeclaration(fieldType, propertyName)
                .WithModifiers(modifiers)
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(new[] { getAccessor, setAccessor })));

            return propertyDeclaration;
        }
    }
}