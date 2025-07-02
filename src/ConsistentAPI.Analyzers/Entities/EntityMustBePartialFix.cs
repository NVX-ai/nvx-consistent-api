using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConsistentAPI.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityMustBePartialFix))]
[Shared]
public class EntityMustBePartialFix : CodeFixProvider
{
  public sealed override ImmutableArray<string> FixableDiagnosticIds =>
    ImmutableArray.Create(EntityMustBePartialAnalyzer.Descriptor.Id);

  public sealed override FixAllProvider GetFixAllProvider() =>
    WellKnownFixAllProviders.BatchFixer;

  public override async Task RegisterCodeFixesAsync(CodeFixContext context)
  {
    if (await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false) is not { } syntaxRoot
        || context.Diagnostics.FirstOrDefault()?.Location.SourceSpan.Start is not { } tokenPosition
        || syntaxRoot
          .FindToken(tokenPosition)
          .Parent?.AncestorsAndSelf()
          .OfType<TypeDeclarationSyntax>()
          .FirstOrDefault() is not { } declaration)
    {
      return;
    }

    context.RegisterCodeFix(
      CodeAction.Create(
        "Add 'partial' modifier",
        c => AddPartialModifierAsync(context.Document, declaration, c),
        nameof(EntityMustBePartialFix)),
      context.Diagnostics.First());
  }

  private static async Task<Document> AddPartialModifierAsync(
    Document document,
    TypeDeclarationSyntax typeDecl,
    CancellationToken cancellationToken)
  {
    if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } syntaxRoot)
    {
      return document;
    }

    var typeWithPartial =
      typeDecl.WithModifiers(typeDecl.Modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword)));

    return document.WithSyntaxRoot(syntaxRoot.ReplaceNode(typeDecl, typeWithPartial));
  }
}
