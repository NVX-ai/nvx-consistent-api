using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nvx.ConsistentAPI.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EntityMustBePartialAnalyzer : DiagnosticAnalyzer
{
  internal static readonly DiagnosticDescriptor Descriptor = new(
    "CNAPI0001",
    "Entity not partial",
    "Entity '{0}' must be partial",
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
      VerifyExternalFoldPartial,
      SyntaxKind.ClassDeclaration,
      SyntaxKind.RecordDeclaration,
      SyntaxKind.StructDeclaration);
  }

  private static void VerifyExternalFoldPartial(SyntaxNodeAnalysisContext context)
  {
    if (context.Node is not TypeDeclarationSyntax typeDeclaration
        || context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not { } typeSymbol
        || !EntityScanner.ImplementsEntity(typeSymbol)
        || typeDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
    {
      return;
    }

    context.ReportDiagnostic(
      Diagnostic.Create(
        Descriptor,
        typeDeclaration.Identifier.GetLocation(),
        typeSymbol.Name));
  }
}
