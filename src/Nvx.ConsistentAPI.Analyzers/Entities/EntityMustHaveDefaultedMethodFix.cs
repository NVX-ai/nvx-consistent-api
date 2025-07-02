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
using Microsoft.CodeAnalysis.Formatting;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Nvx.ConsistentAPI.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityMustHaveDefaultedMethodFix))]
[Shared]
public class EntityMustHaveDefaultedMethodFix : CodeFixProvider
{
  public sealed override ImmutableArray<string> FixableDiagnosticIds =>
    ImmutableArray.Create(EntityMustHaveDefaultedMethodAnalyzer.Descriptor.Id);

  public sealed override FixAllProvider GetFixAllProvider() =>
    WellKnownFixAllProviders.BatchFixer;

  public override async Task RegisterCodeFixesAsync(CodeFixContext context)
  {
    if (await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false) is not { } syntaxRoot
        || context.Diagnostics.FirstOrDefault() is not { } diagnostic
        || syntaxRoot
          .FindToken(diagnostic.Location.SourceSpan.Start)
          .Parent?.AncestorsAndSelf()
          .OfType<TypeDeclarationSyntax>()
          .FirstOrDefault() is not { } declaration)
    {
      return;
    }

    context.RegisterCodeFix(
      CodeAction.Create(
        $"Add '{EntityMustHaveDefaultedMethodAnalyzer.MethodName}' method",
        c => AddFoldMethodAsync(context.Document, declaration, c),
        nameof(EntityMustHaveDefaultedMethodFix)),
      diagnostic);
  }

  private static async Task<Document> AddFoldMethodAsync(
    Document document,
    TypeDeclarationSyntax typeDecl,
    CancellationToken cancellationToken)
  {
    if (await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is not { } semanticModel
        || semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) is not { } typeSymbol)
    {
      return document;
    }

    var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
    return syntaxRoot == null
      ? document
      : document.WithSyntaxRoot(
        syntaxRoot.ReplaceNode(typeDecl, typeDecl.AddMembers(DefaultedMethod(typeSymbol.Name))));
  }

  private static MemberDeclarationSyntax DefaultedMethod(string entityTypeName) =>
    MethodDeclaration(
        IdentifierName(Identifier(entityTypeName)),
        Identifier(EntityMustHaveDefaultedMethodAnalyzer.MethodName)
      )
      .WithModifiers(
        TokenList(
          Token(SyntaxKind.PublicKeyword),
          Token(SyntaxKind.StaticKeyword)
        )
      )
      .WithParameterList(
        ParameterList(
          SeparatedList<ParameterSyntax>(
            [Parameter(Identifier("id")).WithType(IdentifierName("EntityIdType"))]
          )
        )
      )
      .WithExpressionBody(
        ArrowExpressionClause(
          ThrowExpression(
            ObjectCreationExpression(IdentifierName("NotImplementedException")).WithArgumentList(ArgumentList())
          )
        )
      )
      .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))
      .WithAdditionalAnnotations(Formatter.Annotation);
}
