using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Sangria.SourceGeneratorAttributes;

namespace Sangria.SourceGenerators
{
    [Generator]
    public class ShaderProviderGenerator : ISourceGenerator
    {
        private const string ShaderAttribute = nameof(ShaderNameAttribute);
        private const string ShaderPropertiesProviderAttribute = nameof(ShaderPropertiesProviderAttribute);
        private const string ShaderPropertyAttribute = nameof(ShaderPropertyAttribute);

        private readonly List<MemberDeclarationSyntax> m_Members = new();
        private readonly List<MemberDeclarationSyntax> m_Declarations = new();
        private readonly List<StatementSyntax> m_Initializations = new();

        private readonly List<UsingDirectiveSyntax> m_ExtraUsing = new()
        {
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System.Collections.Generic")),
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("UnityEngine")),
        };
        
        // TODO: Implement custom errors.
        // public static readonly DiagnosticDescriptor FailedToParseMessage = new DiagnosticDescriptor(
        //     "SAMPLE001",
        //     "Message title",
        //     "Failed to parse message type '{0}'",
        //     "Parser", DiagnosticSeverity.Error, true);
        
        public void Execute(GeneratorExecutionContext context)
        {
            // context.ReportDiagnostic(Diagnostic.Create(FailedToParseMessage, Location.None, "Sample message type"));
            var classes = context.Compilation.GetClassesByAttributes(ShaderPropertiesProviderAttribute);
            
            m_Members.Clear();
            m_Members.Add(SyntaxFactory.ParseMemberDeclaration("private static readonly Dictionary<int, Shader> Shaders = new();"));
            m_Members.Add(SyntaxFactory.ParseMemberDeclaration("private static readonly Dictionary<int, Material> Materials = new();"));
            
            m_Members.Add(SyntaxFactory.ParseMemberDeclaration("public static Shader GetShader(this int shader) => Shaders[shader];"));
            m_Members.Add(SyntaxFactory.ParseMemberDeclaration("public static Material GetMaterialByShader(this int shader) => Materials[shader];"));
            
            foreach (var classDeclaration in classes)
            {
                //throw exception if class is not static and partial 
                if (!classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword) || !classDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                    throw new Exception("Class with ShaderPropertiesProvider attribute must be static and partial");
            
                m_Declarations.Clear();
                m_Initializations.Clear();
            
                var shaderPropertyFields = classDeclaration.GetConstantsOfTypeByAttribute(ShaderPropertyAttribute, "string");
                foreach (var shaderPropertyField in shaderPropertyFields)
                {
                    var stringFieldName = shaderPropertyField.Declaration.Variables.First().Identifier.Text;
                    var stringFieldValue = shaderPropertyField.Declaration.Variables.First().Identifier.Value;
                    var intFieldName = stringFieldName.ConstToCamelCase() + "PropertyId";
            
                    //add declaration and initialization for shaders
                    m_Declarations.Add(SyntaxFactory.ParseMemberDeclaration($"public static int {intFieldName} {{get; private set;}}"));
                    m_Initializations.Add(SyntaxFactory.ParseStatement($"{intFieldName} = Shader.PropertyToID({stringFieldValue});"));
                }
            
                var shaders = classDeclaration.GetConstantsOfTypeByAttribute(ShaderAttribute, "string");
                foreach (var shader in shaders)
                {
                    var stringField = shader.Declaration.Variables.First().Identifier.Text;
                    var stringFieldValue = shader.Declaration.Variables.First().Identifier.Value;
                    var intField = stringField.ConstToCamelCase() + "ShaderId";
            
                    //add declaration and initialization for shaders
                    m_Declarations.Add(SyntaxFactory.ParseMemberDeclaration($"public static int {intField} {{get; private set;}}"));
                    m_Initializations.Add(SyntaxFactory.ParseStatement($"{intField} = Shader.PropertyToID({stringFieldValue});"));
                    m_Initializations.Add(SyntaxFactory.ParseStatement($"Shaders[{intField}] = Resources.Load<Shader>({stringFieldValue});"));
                    m_Initializations.Add(SyntaxFactory.ParseStatement($"Materials[{intField}] = new Material(Shaders[{intField}]);"));
                }
            
                var staticConstructor = SyntaxFactory.ConstructorDeclaration(classDeclaration.Identifier.Text)
                    .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
                    .WithBody(SyntaxFactory.Block(m_Initializations));
            
                var newClass = classDeclaration
                    .WithMembers(SyntaxFactory.List(m_Declarations))
                    .AddMembers(m_Members.ToArray())
                    .AddMembers(staticConstructor)
                    .WithAttributeLists(SyntaxFactory.List<AttributeListSyntax>())
                    .WithoutTrivia();
            
                var compilationUnit = SyntaxFactory.CompilationUnit()
                    .AddUsings(classDeclaration.GetUsingArr())
                    .AddUsings(m_ExtraUsing.ToArray());
            
                var classWithHierarchy = classDeclaration.CopyHierarchyTo(newClass);
                compilationUnit = compilationUnit.AddMembers(classWithHierarchy);
            
                var code = compilationUnit
                    .NormalizeWhitespace()
                    .ToFullString();
            
                //END OF CODE OF GENERATED CLASS
                context.AddSource(classDeclaration.Identifier.Text + "Generated.cs", SourceText.From(code, Encoding.UTF8));
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            //Dont need it for now
        }
    }
}