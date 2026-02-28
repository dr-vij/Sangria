using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sangria.SourceGenerators
{
    public static class SourceGeneratorHelpers
    {
        private static readonly Regex PrefixRemover = new Regex("^[a-z_]+", RegexOptions.Compiled);
        private const string AttributeSuffix = "Attribute";

        public static string RemovePrefix(this string str)
        {
            return PrefixRemover.Replace(str, "");
        }

        /// <summary>
        /// Changes the TEST_TEXT_EXAMPLE to TestTextExample
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static string ConstToCamelCase(this string str)
        {
            var words = str.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                words[i] = char.ToUpper(word[0]) + word.Substring(1).ToLower();
            }

            return string.Concat(words);
        }

        /// <summary>
        /// Tries to get any declaration of the given type symbol.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to check.</param>
        /// <param name="result">Out parameter that contains the base type declaration syntax if found.</param>
        /// <returns>True if a declaration was found, false otherwise.</returns>
        public static bool TryGetAnyDeclaration(this ITypeSymbol typeSymbol, out BaseTypeDeclarationSyntax result)
        {
            foreach (var syntaxReference in typeSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is BaseTypeDeclarationSyntax syntaxNode)
                {
                    result = syntaxNode;
                    return true;
                }
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Checks if a given type symbol is declared as partial.
        /// </summary>
        /// <param name="typeSymbol">The type symbol to check.</param>
        /// <returns>True if the type symbol is declared as partial, false otherwise.</returns>
        public static bool IsTypeDeclaredPartial(this ITypeSymbol typeSymbol)
        {
            if (typeSymbol.TryGetAnyDeclaration(out var syntaxNode))
            {
                if (syntaxNode.Modifiers.Any(SyntaxKind.PartialKeyword))
                    return true;
            }
            return false;
        }

        public static InterfaceDeclarationSyntax BuildInterface(string name, MemberDeclarationSyntax[] members
        )
        {
            var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(name);
            interfaceDeclaration = interfaceDeclaration.AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
            interfaceDeclaration = interfaceDeclaration.AddMembers(members);
            return interfaceDeclaration;
        }

        /// <summary>
        /// Converts a given attribute name to its short and long forms.
        /// </summary>
        /// <param name="attribute">The attribute name to convert.</param>
        /// <returns>A tuple containing the short and long forms of the attribute name. The short form is the attribute name without the "Attribute" suffix, and the long form is the attribute name with the "Attribute" suffix.</returns>
        private static (string shortName, string longName) StringToAttributeNames(string attribute)
        {
            string longAttribute;

            if (attribute.EndsWith(AttributeSuffix))
            {
                longAttribute = attribute;
                attribute = attribute.Remove(attribute.Length - 9);
            }
            else
            {
                longAttribute = attribute + AttributeSuffix;
            }

            return (attribute, longAttribute);
        }

        public static bool HasAttribute(this MemberDeclarationSyntax attributeListSyntax, string attribute) =>
            HasAttribute(attributeListSyntax, attribute, out _);

        public static bool HasAttribute(this MemberDeclarationSyntax attributeListSyntax, string attribute, out AttributeSyntax result)
        {
            string longAttribute;
            result = null;
            (attribute, longAttribute) = StringToAttributeNames(attribute);

            AttributeSyntax searchResult = null;
            var isSuccess = attributeListSyntax.AttributeLists.Any(list =>
                list.Attributes.Any(attr =>
                {
                    var name = attr.Name.ToString();
                    var found = name == attribute || name == longAttribute;
                    if (found)
                        searchResult = attr;
                    return found;
                }));
            result = searchResult;
            return isSuccess;
        }

        //implementation from https://andrewlock.net/creating-a-source-generator-part-5-finding-a-type-declarations-namespace-and-type-hierarchy/
        /// <summary>
        /// determine the namespace the class/enum/struct is declared in, if any
        /// </summary>
        /// <param name="syntax"></param>
        /// <returns></returns>
        public static string GetNamespaceString(this BaseTypeDeclarationSyntax syntax)
        {
            var nameSpace = string.Empty;
            SyntaxNode potentialNamespaceParent = syntax.Parent;
            while (potentialNamespaceParent != null && !(potentialNamespaceParent is NamespaceDeclarationSyntax))
                potentialNamespaceParent = potentialNamespaceParent.Parent;

            if (potentialNamespaceParent is NamespaceDeclarationSyntax namespaceParent)
            {
                nameSpace = namespaceParent.Name.ToString();
                while (true)
                {
                    if (!(namespaceParent.Parent is NamespaceDeclarationSyntax parent))
                        break;
                    nameSpace = $"{namespaceParent.Name}.{nameSpace}";
                    namespaceParent = parent;
                }
            }

            return nameSpace;
        }
    }
}