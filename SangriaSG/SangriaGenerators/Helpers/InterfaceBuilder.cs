using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Sangria.SourceGeneratorAttributes;

namespace Sangria.SourceGenerators
{
    public class InterfaceBuilder
    {
        private readonly HashSet<string> m_UsedSignatures = new();

        public string Key { get; }
        public ITypeSymbol ReferenceInterface { get; }

        private readonly List<MemberDeclarationSyntax> m_Members = new();

        private void AddMember(MemberDeclarationSyntax member)
        {
            if (member != null) m_Members.Add(member);
        }

        public InterfaceBuilder(string key, ITypeSymbol referenceInterface)
        {
            Key = key;
            ReferenceInterface = referenceInterface;
        }

        public void AddInterfaceSubscriptionMethod(TypeSyntax fieldType, string methodName, Visibility methodVisibility)
        {
            var key = CreateKey(fieldType, methodName);
            if (!m_UsedSignatures.Add(key))
                return;

            AddMember(CreateInterfaceSubscriptionMethod(fieldType, methodName, methodVisibility));
        }

        public void AddInterfaceSubscriptionEvent(TypeSyntax fieldType, string eventName, Visibility eventVisibility)
        {
            var key = CreateKey(fieldType, eventName);
            if (!m_UsedSignatures.Add(key))
                return;

            AddMember(CreateInterfaceSubscriptionEvent(fieldType, eventName, eventVisibility));
        }

        public void AddInterfaceProperty(TypeSyntax fieldType, string propertyName, Visibility getterVisibility, Visibility setterVisibility)
        {
            var key = CreateKey(fieldType, propertyName);
            if (!m_UsedSignatures.Add(key))
                return;

            AddMember(CreateInterfaceProperty(fieldType, propertyName, getterVisibility, setterVisibility));
        }

        private string CreateKey(TypeSyntax fieldType, string methodName) => $"{fieldType}{methodName}";

        /// <summary>
        /// Converts the reference interface to a simple base type syntax.
        /// </summary>
        /// <returns>A <see cref="SimpleBaseTypeSyntax"/> representing the reference interface.</returns>
        public SimpleBaseTypeSyntax ToSimpleBaseTypeSyntax()
        {
            var interfaceIdentifier = ReferenceInterface.Name;
            var interfaceType = SyntaxFactory.ParseTypeName(interfaceIdentifier);
            return SyntaxFactory.SimpleBaseType(interfaceType);
        }

        /// <summary>
        /// Converts the interface builder container to a syntax node.
        /// </summary>
        /// <returns>A <see cref="MemberDeclarationSyntax"/> representing the interface builder container.</returns>
        public MemberDeclarationSyntax ToSyntaxNode()
        {
            var interfaceNode = SyntaxFactory.InterfaceDeclaration(ReferenceInterface.Name)
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.PartialKeyword)))
                .WithMembers(SyntaxFactory.List(m_Members));

            ReferenceInterface.TryGetAnyDeclaration(out var declaration);
            return declaration.CopyHierarchyTo(interfaceNode);
        }

        /// <summary>
        /// Creates a method declaration for an interface that allows subscribing to an event with optional initial call parameter.
        /// </summary>
        /// <param name="fieldType">The type of the event's data.</param>
        /// <param name="methodName">The name of the method to be created.</param>
        /// <param name="methodVisibility">The visibility of the method.</param>
        /// <returns>A MethodDeclarationSyntax object representing the created method.</returns>
        public static MethodDeclarationSyntax CreateInterfaceSubscriptionMethod(TypeSyntax fieldType, string methodName, Visibility methodVisibility)
        {
            if (methodVisibility == Visibility.Public)
            {
                var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(methodVisibility.ToVisibilitySyntaxKind()));

                var returnType = SyntaxFactory.IdentifierName("IDisposable");
                var parameterType = SyntaxFactory.GenericName(SyntaxFactory.Identifier("EventHandler"))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(fieldType)));
                
                // Create first parameter - handler
                var handlerParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("handler")).WithType(parameterType);

                // Create second parameter - initialCall with default value true
                var initialCallParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("initialCall"))
                    .WithType(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.BoolKeyword)))
                    .WithDefault(SyntaxFactory.EqualsValueClause(
                        SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));

                // Create parameter list with both parameters
                var parameters = SyntaxFactory.ParameterList(
                    SyntaxFactory.SeparatedList(new[] { handlerParameter, initialCallParameter }));

                var methodDeclaration = SyntaxFactory.MethodDeclaration(returnType, methodName)
                    .WithModifiers(modifiers)
                    .WithParameterList(parameters)
                    .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));

                return methodDeclaration;
            }
            return null;
        }

        /// <summary>
        /// Creates an event field declaration for an interface.
        /// </summary>
        /// <param name="fieldType">The type of the event's data.</param>
        /// <param name="eventName">The name of the event to be created.</param>
        /// <param name="eventVisibility">The visibility of the event.</param>
        /// <returns>An EventFieldDeclarationSyntax object representing the created event field.</returns>
        public static EventFieldDeclarationSyntax CreateInterfaceSubscriptionEvent(TypeSyntax fieldType, string eventName, Visibility eventVisibility)
        {
            if (eventVisibility == Visibility.Public)
            {
                var genericType = $"EventHandler<{fieldType}>";
                var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(eventVisibility.ToVisibilitySyntaxKind()));
                return SyntaxFactory.EventFieldDeclaration(
                        SyntaxFactory.VariableDeclaration(SyntaxFactory.ParseTypeName(genericType))
                            .WithVariables(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(eventName))))
                    .WithModifiers(modifiers);
            }

            return null;
        }

        /// <summary>
        /// Creates a property with simple get and set accessors.
        /// </summary>
        /// <param name="fieldType">The type of the property.</param>
        /// <param name="propertyName">The name of the property.</param>
        /// <param name="getterVisibility">The visibility of the getter.</param>
        /// <param name="setterVisibility">The visibility of the setter.</param>
        /// <returns>A PropertyDeclarationSyntax representing the created property.</returns>
        public static PropertyDeclarationSyntax CreateInterfaceProperty(TypeSyntax fieldType, string propertyName, Visibility getterVisibility, Visibility setterVisibility)
        {
            if (getterVisibility == Visibility.Public || setterVisibility == Visibility.Public)
            {
                var propertyModifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
                List<AccessorDeclarationSyntax> accessors = new();
                if (getterVisibility == Visibility.Public)
                {
                    var getAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                    accessors.Add(getAccessor);
                }
                
                if (setterVisibility == Visibility.Public)
                {
                    var setAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
                    accessors.Add(setAccessor);
                }

                var propertyDeclaration = SyntaxFactory.PropertyDeclaration(fieldType, propertyName)
                    .WithModifiers(propertyModifiers)
                    .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));

                return propertyDeclaration;
            }

            return null;
        }
    }
}