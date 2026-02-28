using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sangria.SourceGenerators
{
    public static class ClassDeclarationHelpers
    {
        /// <summary>
        /// Copies the hierarchy of the example class to the new class. This includes namespaces and parent classes.
        /// </summary>
        /// <param name="referenceHierarchy">The class whose hierarchy is to be copied.</param>
        /// <param name="newMember">The class to which the hierarchy is to be copied.</param>
        /// <returns>The new class with the copied hierarchy.</returns>
        public static MemberDeclarationSyntax CopyHierarchyTo(this BaseTypeDeclarationSyntax referenceHierarchy, BaseTypeDeclarationSyntax newMember)
        {
            var ancestors = referenceHierarchy.Ancestors();
            MemberDeclarationSyntax result = newMember;
            foreach (var ancestor in ancestors)
            {
                switch (ancestor)
                {
                    case NamespaceDeclarationSyntax namespaceDeclaration:
                        result = namespaceDeclaration.WithMembers(SyntaxFactory.List(new[] { result })).WithoutTrivia();
                        break;
                    case ClassDeclarationSyntax classDeclaration:
                        result = classDeclaration.WithMembers(SyntaxFactory.List(new[] { result })).WithoutTrivia();
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Retrieves all syntax nodes of a specific type from the given compilation.
        /// </summary>
        /// <typeparam name="T">The type of syntax nodes to retrieve.</typeparam>
        /// <param name="compilation">The compilation to retrieve the syntax nodes from.</param>
        /// <returns>An IEnumerable of syntax nodes of the specified type.</returns>
        public static IEnumerable<T> GetNodesOfType<T>(this Compilation compilation) where T : SyntaxNode
        {
            return compilation.GetNodes().OfType<T>();
        }

        /// <summary>
        /// Retrieves all syntax nodes of a specific type from the given compilation that satisfy a specified condition.
        /// </summary>
        /// <typeparam name="T">The type of syntax nodes to retrieve.</typeparam>
        /// <param name="compilation">The compilation to retrieve the syntax nodes from.</param>
        /// <param name="predicate">A function to test each syntax node for a condition.</param>
        /// <returns>An IEnumerable of syntax nodes of the specified type that satisfy the condition specified by predicate.</returns>
        public static IEnumerable<T> GetNodesOfType<T>(this Compilation compilation, Func<T, bool> predicate) where T : SyntaxNode
        {
            return compilation.GetNodesOfType<T>().Where(predicate);
        }

        /// <summary>
        /// Retrieves all syntax nodes from the given compilation.
        /// </summary>
        /// <param name="compilation">The compilation to retrieve the syntax nodes from.</param>
        /// <returns>An IEnumerable of all syntax nodes in the compilation.</returns>
        public static IEnumerable<SyntaxNode> GetNodes(this Compilation compilation)
        {
            return compilation.SyntaxTrees
                .SelectMany(tree => tree.GetRoot().DescendantNodes());
        }

        /// <summary>
        /// Retrieves the using directives of the given class declaration.
        /// </summary>
        /// <param name="syntaxNode">The class declaration from which to retrieve the using directives.</param>
        /// <returns>An array of using directive syntaxes if any exist, otherwise an empty array.</returns>
        public static UsingDirectiveSyntax[] GetUsingArr(this SyntaxNode syntaxNode)
        {
            return syntaxNode.GetUsing()?.ToArray() ?? Array.Empty<UsingDirectiveSyntax>();
        }

        /// <summary>
        /// Retrieves the using directives of the given class declaration.
        /// </summary>
        /// <param name="syntaxNode">The class declaration from which to retrieve the using directives.</param>
        /// <returns>A syntax list of using directive syntaxes if any exist, otherwise null.</returns>
        public static SyntaxList<UsingDirectiveSyntax>? GetUsing(this SyntaxNode syntaxNode)
        {
            return syntaxNode.AncestorsAndSelf()
                .OfType<CompilationUnitSyntax>()
                .FirstOrDefault()?
                .Usings;
        }

        /// <summary>
        /// Retrieves all class declarations and their corresponding using directives from the given compilation.
        /// </summary>
        /// <param name="compilation">The compilation from which to retrieve the class declarations and using directives.</param>
        /// <returns>An enumerable of tuples, each containing a class declaration and its corresponding using directives.</returns>
        public static IEnumerable<(ClassDeclarationSyntax Class, SyntaxList<UsingDirectiveSyntax>? Usings)> GetClassWithUsing(this Compilation compilation)
        {
            return compilation.GetNodesOfType<ClassDeclarationSyntax>()
                .Select(classNode => (
                    Class: classNode,
                    Usings: classNode.GetUsing()
                ));
        }
    }
}