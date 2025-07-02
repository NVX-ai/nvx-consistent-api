using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Nvx.ConsistentAPI.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class EntityMustHaveStreamPrefixAnalyzer : DiagnosticAnalyzer
{
  internal const string ConstantName = "StreamPrefix";

  internal static readonly DiagnosticDescriptor Descriptor = new(
    "CNAPI0004",
    "Entity missing stream prefix",
    $"Entity '{{0}}' needs to have a public string constant called {ConstantName}",
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
      VerifyEntityShouldHaveStreamPrefix,
      SyntaxKind.ClassDeclaration,
      SyntaxKind.RecordDeclaration,
      SyntaxKind.StructDeclaration);
  }

  private static void VerifyEntityShouldHaveStreamPrefix(SyntaxNodeAnalysisContext context)
  {
    if (context.Node is not TypeDeclarationSyntax typeDeclaration
        || context.SemanticModel.GetDeclaredSymbol(typeDeclaration) is not { } typeSymbol
        || !EntityScanner.ImplementsEntity(typeSymbol)
        || HasStreamPrefixConstant(typeSymbol))
    {
      return;
    }

    context.ReportDiagnostic(
      Diagnostic.Create(
        Descriptor,
        typeDeclaration.Identifier.GetLocation(),
        typeSymbol.Name));
  }

  private static bool HasStreamPrefixConstant(INamedTypeSymbol typeSymbol) =>
    typeSymbol
      .GetMembers(ConstantName)
      .Any(member => member is IFieldSymbol { IsConst: true, Type.SpecialType: SpecialType.System_String });
}
