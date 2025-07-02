using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ConsistentAPI.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EntityMustHaveDefaultedMethodAnalyzer : DiagnosticAnalyzer
{
  public const string MethodName = "Defaulted";

  internal static readonly DiagnosticDescriptor Descriptor = new(
    "CNAPI0003",
    $"Missing {MethodName} method",
    $"Type '{{0}}' implements EventModelEntity<{{0}}> but does not have the required static {MethodName} method",
    "Design",
    DiagnosticSeverity.Error,
    true);

  public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Descriptor);

  public override void Initialize(AnalysisContext context)
  {
    context.ConfigureGeneratedCodeAnalysis(
      GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
    context.EnableConcurrentExecution();

    context.RegisterSyntaxNodeAction(
      VerifyExternalFoldShouldFoldMethod,
      SyntaxKind.ClassDeclaration,
      SyntaxKind.RecordDeclaration,
      SyntaxKind.StructDeclaration);
  }

  private static void VerifyExternalFoldShouldFoldMethod(SyntaxNodeAnalysisContext context)
  {
    if (context.Node is not TypeDeclarationSyntax typeDeclaration
        || context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not { } typeSymbol
        || HasMatchingMethod(typeSymbol)
        || !ImplementsEventModelEntity(typeSymbol))
    {
      return;
    }

    context.ReportDiagnostic(Diagnostic.Create(Descriptor, typeDeclaration.Identifier.GetLocation(), typeSymbol.Name));
  }

  private static bool ImplementsEventModelEntity(INamedTypeSymbol typeSymbol) =>
    typeSymbol
      .AllInterfaces
      .Any(i => i.OriginalDefinition.ContainingNamespace.ToDisplayString() == "ConsistentAPI"
                && i.OriginalDefinition is { Name: "EventModelEntity", Arity: 1 });

  private static bool HasMatchingMethod(
    INamedTypeSymbol entityType) =>
    entityType
      .GetMembers(MethodName)
      .Any(member =>
        member is IMethodSymbol { IsStatic: true } methodSymbol
        && IsValidReturnType(methodSymbol, entityType)
        && HasCorrectParameters(methodSymbol));

  private static bool HasCorrectParameters(IMethodSymbol methodSymbol) =>
    methodSymbol.Parameters.Length == 1
    && methodSymbol.Parameters[0].Type.BaseType?.ContainingNamespace.ToDisplayString() == "ConsistentAPI"
    && methodSymbol.Parameters[0].Type.BaseType?.Name == "StrongId";

  private static bool IsValidReturnType(IMethodSymbol methodSymbol, ITypeSymbol entityType) =>
    SymbolEqualityComparer.Default.Equals(methodSymbol.ReturnType, entityType);
}
