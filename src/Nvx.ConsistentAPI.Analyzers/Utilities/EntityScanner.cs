using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class EntityScanner
{
  public static IncrementalValuesProvider<INamedTypeSymbol> GetEntities(
    this IncrementalGeneratorInitializationContext context) =>
    context
      .SyntaxProvider.CreateSyntaxProvider(
        (node, _) => node is TypeDeclarationSyntax,
        (ctx, _) => ctx.Node is TypeDeclarationSyntax typeDeclaration
                    && ctx.SemanticModel.GetDeclaredSymbol(typeDeclaration) is { } typeSymbol
                    && ImplementsEntity(typeSymbol)
          ? typeSymbol
          : null)
      .Where(r => r is not null)
      .Select((r, _) => r!);

  public static bool ImplementsEntity(INamedTypeSymbol typeSymbol) =>
  (
    from i in typeSymbol.Interfaces
    where i.OriginalDefinition.ContainingNamespace.ToDisplayString() == "Nvx.ConsistentAPI"
    where i.OriginalDefinition.Name == "EventModelEntity"
    where i.OriginalDefinition.Arity == 1
    select i
  ).Any();

  public static IEnumerable<ITypeSymbol> RegularFoldEvents(this INamedTypeSymbol entityType) =>
    GetFoldEventsFromInterface(entityType, "Folds");

  public static IEnumerable<ITypeSymbol> ExternalFoldEvents(this INamedTypeSymbol entityType) =>
    GetFoldEventsFromInterface(entityType, "FoldsExternally");

  private static IEnumerable<ITypeSymbol> GetFoldEventsFromInterface(
    this INamedTypeSymbol entityType,
    string interfaceName) =>
    from interfaceSymbol in entityType.AllInterfaces
    where interfaceSymbol.Name == interfaceName
    where interfaceSymbol.Arity == 2
    where interfaceSymbol.ContainingNamespace.ToDisplayString() == "Nvx.ConsistentAPI"
    select interfaceSymbol.TypeArguments[0];
}
