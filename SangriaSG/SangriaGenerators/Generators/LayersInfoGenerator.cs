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
    public class LayersInfoGenerator : ISourceGenerator
    {
        private const string LayerMaskStr = "Mask";
        private const string Prefix = "m_";
        private const string LayerEnding = "Name";

        private List<MemberDeclarationSyntax> m_Properties = new();
        private List<MemberDeclarationSyntax> m_Fields = new();
        private List<ExpressionStatementSyntax> m_Setters = new();

        private readonly List<UsingDirectiveSyntax> m_ExtraUsing = new()
        {
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("UnityEngine")),
        };

        public void Initialize(GeneratorInitializationContext context) { }

        public void Execute(GeneratorExecutionContext context)
        {
            var compilation = context.Compilation;
            var attribute = nameof(ExportLayerInfo);
            var attributes = new[] { attribute };
            var classNodes = compilation.GetClassesByFieldAttributes(attributes);

            // context.AddSource("test" + $"Gen.cs", SourceText.From("", Encoding.UTF8));

            foreach (var classNode in classNodes)
            {
                var className = classNode.Identifier.Text;
                m_Properties.Clear();
                m_Setters.Clear();
                m_Fields.Clear();

                var fieldNodes = classNode.Members
                    .OfType<FieldDeclarationSyntax>()
                    .Where(fieldNode => attributes.Any(fieldNode.HasAttribute));

                var newClass = classNode.WithMembers(SyntaxFactory.List<MemberDeclarationSyntax>())
                    .WithoutTrivia()
                    .WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>())
                    .WithBaseList(null);

                foreach (var fieldNode in fieldNodes)
                {
                    var fieldName = fieldNode.Declaration.Variables.First().Identifier.Text;
                    var basePropertyName = fieldName.RemovePrefix().Replace(LayerEnding, string.Empty);

                    var layerProperty = GenerateProperty(basePropertyName, String.Empty);
                    var maskProperty = GenerateProperty(basePropertyName, LayerMaskStr);

                    var layerField = SyntaxFactory.ParseMemberDeclaration($@"private int {Prefix}{basePropertyName};");
                    var maskField = SyntaxFactory.ParseMemberDeclaration($@"private int {Prefix}{basePropertyName}{LayerMaskStr};");
                    var layerGetter = SyntaxFactory.ParseMemberDeclaration($@"public string {fieldName.RemovePrefix()} => {fieldName};");

                    m_Fields.Add(layerField);
                    m_Fields.Add(maskField);
                    m_Properties.Add(layerProperty);
                    m_Properties.Add(maskProperty);
                    m_Properties.Add(layerGetter);

                    var layerSet = GenerateLayerSetterCode(basePropertyName);
                    var maskSet = GenerateMaskSetterCode(basePropertyName);
                    m_Setters.Add(layerSet);
                    m_Setters.Add(maskSet);
                }

                var initOnce = GenerateInitOnceMethod(m_Setters);
                var initOnceField = GenerateIsInitializedField();

                newClass = newClass
                    .AddMembers(m_Fields.ToArray())
                    .AddMembers(m_Properties.ToArray())
                    .AddMembers(initOnce, initOnceField);

                var combinedUsing = classNode
                    .GetUsingArr()
                    .Concat(m_ExtraUsing)
                    .MakeDistinct()
                    .ToArray();

                var compilationUnit = SyntaxFactory.CompilationUnit()
                    .AddUsings(combinedUsing);

                var classWithHierarchy = classNode.CopyHierarchyTo(newClass);
                compilationUnit = compilationUnit
                    .AddMembers(classWithHierarchy);

                var code = compilationUnit
                    .NormalizeWhitespace()
                    .ToFullString();
                context.AddSource(className + $"Gen.cs", SourceText.From(code, Encoding.UTF8));
            }
        }

        /// <summary>
        /// Generates code like this:
        /// m_NormalLayer = LayerMask.NameToLayer(m_NormalLayerName);
        /// </summary>
        /// <param name="basePropertyName"></param>
        /// <returns></returns>
        private ExpressionStatementSyntax GenerateLayerSetterCode(string basePropertyName)
        {
            var fieldName = Prefix + basePropertyName;
            var initialField = Prefix + basePropertyName + LayerEnding;
            var code = $"{fieldName} = LayerMask.NameToLayer({initialField});";
            return SyntaxFactory.ParseStatement(code) as ExpressionStatementSyntax;
        }

        /// <summary>
        /// Generates code like this:
        /// 
        /// </summary>
        /// <param name="basePropertyName"></param>
        /// <returns></returns>
        private ExpressionStatementSyntax GenerateMaskSetterCode(string basePropertyName)
        {
            var fieldName = Prefix + basePropertyName + LayerMaskStr;
            var initialField = Prefix + basePropertyName + LayerEnding;
            var code = $"{fieldName} = LayerMask.GetMask({initialField});";
            return SyntaxFactory.ParseStatement(code) as ExpressionStatementSyntax;
        }

        private FieldDeclarationSyntax GenerateIsInitializedField()
        {
            var code = "private bool m_IsInitialized;";
            return SyntaxFactory.ParseMemberDeclaration(code) as FieldDeclarationSyntax;
        }

        private MethodDeclarationSyntax GenerateInitOnceMethod(List<ExpressionStatementSyntax> expressions)
        {
            var statements = new List<StatementSyntax>
            {
                SyntaxFactory.IfStatement(
                    SyntaxFactory.IdentifierName("m_IsInitialized"),
                    SyntaxFactory.Block(SyntaxFactory.ReturnStatement()))
            };

            statements.AddRange(expressions);

            statements.Add(
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName("m_IsInitialized"),
                        SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                    )
                )
            );

            return SyntaxFactory.MethodDeclaration(
                    SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)),
                    "InitOnce")
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword))
                .WithBody(SyntaxFactory.Block(statements));
        }

        /// <summary>
        /// Creates property like this:
        /// public int {basePropertyName}
        /// {
        ///     get
        ///     {
        ///         InitOnce();
        ///         return m_{basePropertyName};
        ///     }
        /// }
        /// </summary>
        /// <param name="basePropertyName"></param>
        /// <param name="suffix"></param>
        /// <returns></returns>
        private PropertyDeclarationSyntax GenerateProperty(string basePropertyName, string suffix)
        {
            var fieldName = "m_" + basePropertyName + suffix;
            var propertyName = basePropertyName + suffix;

            return SyntaxFactory.PropertyDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)), propertyName)
                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                .AddAccessorListAccessors(
                    SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                        .WithBody(SyntaxFactory.Block(
                            SyntaxFactory.ExpressionStatement(SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("InitOnce"))),
                            SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(fieldName))
                        ))
                );
        }
    }
}