using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UTools.SourceGeneratorAttributes;

namespace UTools.SourceGenerators
{
    public static class AttributeHelpers
    {
        public static SyntaxKind ToVisibilitySyntaxKind(this Visibility visibility)
        {
            return visibility switch
            {
                Visibility.Public => SyntaxKind.PublicKeyword,
                Visibility.Protected => SyntaxKind.ProtectedKeyword,
                Visibility.Private => SyntaxKind.PrivateKeyword,
                Visibility.Internal => SyntaxKind.InternalKeyword,
                _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, null)
            };
        }
        
        public static string GetLastWord(this string str)
        {
            var lastComma = str.LastIndexOf('.');
            return lastComma == -1 ? str : str.Substring(lastComma + 1);
        }
        
        /// <summary>
        ///  Get all fields of a class that are constants and have a specific attribute
        /// </summary>
        /// <param name="classDeclaration"></param>
        /// <param name="attribute"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static IEnumerable<FieldDeclarationSyntax> GetConstantsOfTypeByAttribute(
            this ClassDeclarationSyntax classDeclaration,
            string attribute,
            string type)
        {
            return classDeclaration.Members.OfType<FieldDeclarationSyntax>().Where(
                fieldDeclaration =>
                {
                    var hasAttr = fieldDeclaration.HasAttribute(attribute);
                    var isConst = fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword);
                    var isStringType = fieldDeclaration.Declaration.Type.ToString() == type;
                    return hasAttr && isConst && isStringType;
                }
            );
        }

        /// <summary>
        ///  Get all classes that have a specific attribute on a field
        /// </summary>
        /// <param name="compilation"></param>
        /// <param name="attributes"></param>
        /// <returns></returns>
        public static IEnumerable<ClassDeclarationSyntax> GetClassesByFieldAttributes(this Compilation compilation, params string[] attributes)
        {
            return compilation.GetNodesOfType<ClassDeclarationSyntax>()
                .Where(classDec => classDec.Members.OfType<FieldDeclarationSyntax>()
                    .Any(field => attributes.Any(field.HasAttribute)));
        }

        /// <summary>
        ///  Get all classes that have a specific attribute
        /// </summary>
        /// <param name="compilation"></param>
        /// <param name="attributes"></param>
        /// <returns></returns>
        public static IEnumerable<ClassDeclarationSyntax> GetClassesByAttributes(this Compilation compilation, params string[] attributes)
        {
            return compilation.GetNodesOfType<ClassDeclarationSyntax>()
                .Where(classDec => attributes.Any(classDec.HasAttribute));
        }


        public static bool TryGetAttribute(this MemberDeclarationSyntax memberNode, Compilation compilation, string attributeName, out AttributeSyntax result)
        {
            var model = compilation.GetSemanticModel(memberNode.SyntaxTree);
            var attributes = memberNode.AttributeLists.SelectMany(attrList => attrList.Attributes);
            foreach (var attribute in attributes)
            {
                var attributeSymbol = model.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
                if (attributeSymbol?.ContainingType.Name == attributeName)
                {
                    result = attribute;
                    return true;
                }
            }

            result = null;
            return false;
        }
        
        public static bool TryGetAttributeParameter(
            this FieldDeclarationSyntax fieldNode,
            Compilation compilation,
            string attributeName,
            string parameterName,
            out ExpressionSyntax result)
        {
            var attrFound = fieldNode.TryGetAttribute(compilation, attributeName, out var attribute);
            
            if (attrFound)
            {
                var model = compilation.GetSemanticModel(attribute.SyntaxTree);
                var attributeSymbol = model.GetSymbolInfo(attribute).Symbol as IMethodSymbol;

                var index = -1;
                foreach (var parameter in attributeSymbol.Parameters)
                {
                    var name = parameter.Name;
                    name = char.ToUpper(name[0]) + name.Substring(1);

                    if (name == parameterName)
                    {
                        index = parameter.Ordinal;
                        break;
                    }
                }
                
                if (attribute.ArgumentList != null)
                {
                    if (index != -1)
                    {
                        result = attribute.ArgumentList.Arguments[index].Expression;
                        return true;
                    }
                    
                    foreach (var argument in attribute.ArgumentList.Arguments)
                    {
                        if (argument.NameEquals?.Name.Identifier.Text == parameterName)
                        { 
                            result = argument.Expression;
                            return true;
                        }
                    }
                }
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Tries to get the type symbols from the attribute interface property of a field node.
        /// </summary>
        /// <param name="compilation">The compilation that contains the field node.</param>
        /// <param name="fieldNode">The field node to get the attribute interface property from.</param>
        /// <param name="attributeName">The name of the attribute to look for.</param>
        /// <param name="results">The list of type symbols found in the attribute interface property. This is an output parameter.</param>
        /// <returns>Returns true if one or more type symbols were found, otherwise false.</returns>
        /// <remarks>
        /// This method looks for an attribute with the specified name on the field node. If found, it retrieves the type symbols from the attribute interface property.
        /// The type symbols are added to the 'results' list. If no type symbols are found, the 'results' list is empty.
        /// </remarks>
        public static bool TryGetTypeFromAttributeInterfaceProperty(
            this Compilation compilation,
            FieldDeclarationSyntax fieldNode,
            string attributeName,
            out List<ITypeSymbol> results)
        {
            results = new List<ITypeSymbol>();
            var model = compilation.GetSemanticModel(fieldNode.SyntaxTree);
            var attributes = fieldNode.AttributeLists.SelectMany(attrList => attrList.Attributes);

            foreach (var attribute in attributes)
            {
                var attributeSymbol = model.GetSymbolInfo(attribute).Symbol as IMethodSymbol;
                if (attributeSymbol?.ContainingType.Name != attributeName)
                    continue;
                if (attribute.ArgumentList == null)
                    continue;

                foreach (var arg in attribute.ArgumentList.Arguments)
                {
                    if (arg.NameEquals != null)
                    {
                        if (arg.Expression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
                        {
                            foreach (var expr in implicitArrayCreation.Initializer.Expressions.OfType<TypeOfExpressionSyntax>())
                                AddTypeSymbolFromTypeOfExpression(model, results, expr);
                        }
                        else if (arg.Expression is ArrayCreationExpressionSyntax arrayCreation)
                        {
                            if (arrayCreation.Initializer != null)
                                foreach (var expr in arrayCreation.Initializer.Expressions.OfType<TypeOfExpressionSyntax>())
                                    AddTypeSymbolFromTypeOfExpression(model, results, expr);
                        }
                    }
                    else if (arg.Expression is TypeOfExpressionSyntax typeOfExpression)
                    {
                        AddTypeSymbolFromTypeOfExpression(model, results, typeOfExpression);
                    }
                }
            }

            return results.Count > 0;
        }

        private static void AddTypeSymbolFromTypeOfExpression(SemanticModel model, List<ITypeSymbol> results, TypeOfExpressionSyntax typeOfExpression)
        {
            var typeSymbol = model.GetTypeInfo(typeOfExpression.Type).Type;
            if (typeSymbol is { TypeKind: TypeKind.Interface })
                results.Add(typeSymbol);
            else
                throw new InvalidOperationException("Specified type must be an interface.");
        }
    }
}