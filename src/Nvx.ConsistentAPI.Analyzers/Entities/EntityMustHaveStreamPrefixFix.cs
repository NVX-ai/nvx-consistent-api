using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Nvx.ConsistentAPI.Analyzers;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(EntityMustHaveStreamPrefixFix))]
[Shared]
public class EntityMustHaveStreamPrefixFix : CodeFixProvider
{
  public sealed override ImmutableArray<string> FixableDiagnosticIds =>
    ImmutableArray.Create(EntityMustHaveStreamPrefixAnalyzer.Descriptor.Id);

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
        $"Add '{EntityMustHaveStreamPrefixAnalyzer.ConstantName}' constant",
        c => AddStreamPrefixConstantAsync(context.Document, declaration, c),
        nameof(EntityMustHaveStreamPrefixFix)),
      context.Diagnostics.First());
  }

  private static async Task<Document> AddStreamPrefixConstantAsync(
    Document document,
    TypeDeclarationSyntax typeDecl,
    CancellationToken cancellationToken)
  {
    if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } syntaxRoot)
    {
      return document;
    }

    var typeName = typeDecl.Identifier.ValueText;
    var kebabCaseName = ConvertToKebabCase(typeName);

    if (!kebabCaseName.EndsWith("-"))
    {
      kebabCaseName += "-";
    }

    if (!kebabCaseName.EndsWith("-entity-"))
    {
      kebabCaseName += "entity-";
    }

    var constField = SyntaxFactory
      .FieldDeclaration(
        SyntaxFactory
          .VariableDeclaration(
            SyntaxFactory.PredefinedType(
              SyntaxFactory.Token(SyntaxKind.StringKeyword)))
          .WithVariables(
            SyntaxFactory.SingletonSeparatedList(
              SyntaxFactory
                .VariableDeclarator(
                  SyntaxFactory.Identifier(EntityMustHaveStreamPrefixAnalyzer.ConstantName))
                .WithInitializer(
                  SyntaxFactory.EqualsValueClause(
                    SyntaxFactory.LiteralExpression(
                      SyntaxKind.StringLiteralExpression,
                      SyntaxFactory.Literal(kebabCaseName)))))))
      .WithModifiers(
        SyntaxFactory.TokenList(
          SyntaxFactory.Token(SyntaxKind.PublicKeyword),
          SyntaxFactory.Token(SyntaxKind.ConstKeyword)))
      .WithAdditionalAnnotations(Formatter.Annotation);

    var newTypeDecl = typeDecl.AddMembers(constField);
    var newRoot = syntaxRoot.ReplaceNode(typeDecl, newTypeDecl);

    return document.WithSyntaxRoot(newRoot);
  }

  private static string ConvertToKebabCase(string input) =>
    string.IsNullOrEmpty(input)
      ? string.Empty
      : Regex
        .Replace(input, "(?<!^)([A-Z][a-z]|(?<=[a-z])[A-Z])", "-$1")
        .ToLowerInvariant();
}
