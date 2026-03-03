using Microsoft.CodeAnalysis;

namespace Sangria.SourceGenerators
{
    public static class Diagnostics
    {
        public static readonly DiagnosticDescriptor UnexpectedError = new DiagnosticDescriptor(
            id: "SANGRIA001",
            title: "Unexpected Generator Error",
            messageFormat: "Generator '{0}' failed with error: {1}",
            category: "SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ClassMustBeStaticAndPartial = new DiagnosticDescriptor(
            id: "SANGRIA002",
            title: "Class must be static and partial",
            messageFormat: "Class '{0}' decorated with '{1}' must be both static and partial",
            category: "SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ClassMustBePartial = new DiagnosticDescriptor(
            id: "SANGRIA003",
            title: "Class must be partial",
            messageFormat: "Class '{0}' decorated with '{1}' must be partial",
            category: "SourceGenerator",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
