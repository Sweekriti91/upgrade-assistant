using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace HttpContextMover
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(HttpContextMoverCodeFixProvider)), Shared]
    public class HttpContextMoverCodeFixProvider : CodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(HttpContextMoverAnalyzer.DiagnosticId); }
        }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/FixAllProvider.md for more information on Fix All Providers
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            //// Find the type declaration identified by the diagnostic.
            var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);
            var method = node.Parent.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();

            if (method is null)
            {
                return;
            }

            var semantic = await context.Document.GetSemanticModelAsync(context.CancellationToken);
            var symbol = semantic.GetSymbolInfo(node);

            if (symbol.Symbol is not IPropertySymbol property)
            {
                return;
            }

            //// Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.CodeFixTitle,
                    createChangedSolution: c => MakeUppercaseAsync(context.Document, property, node, method, c),
                    equivalenceKey: nameof(CodeFixResources.CodeFixTitle)),
                diagnostic);
        }

        private async Task<Solution> MakeUppercaseAsync(Document document, IPropertySymbol property, SyntaxNode node, MethodDeclarationSyntax methodDecl, CancellationToken cancellationToken)
        {
            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken);
            //var callers = await Microsoft.CodeAnalysis.FindSymbols.SymbolFinder.FindCallersAsync(methodSymbol, document.Project.Solution, cancellationToken);

            var slnEditor = new SolutionEditor(document.Project.Solution);
            var editor = await slnEditor.GetDocumentEditorAsync(document.Id, cancellationToken);

            // Add parameter if not available
            var parameter = methodDecl.ParameterList.Parameters.FirstOrDefault(p =>
            {
                var symbol = semanticModel.GetSymbolInfo(p.Type);

                return SymbolEqualityComparer.IncludeNullability.Equals(symbol.Symbol, property.Type);
            });

            var propertyTypeSyntaxNode = editor.Generator.NameExpression(property.Type);

            if (parameter is null)
            {
                var ps = editor.Generator.GetParameters(methodDecl);
                var current = editor.Generator.IdentifierName("currentContext");
                parameter = (ParameterSyntax)editor.Generator.ParameterDeclaration("currentContext", propertyTypeSyntaxNode);

                editor.AddParameter(methodDecl, parameter);
            }

            // Update node usage
            var name = editor.Generator.IdentifierName(parameter.Identifier.Text);

            editor.ReplaceNode(node, name);

            await UpdateCallers(methodSymbol, slnEditor, cancellationToken);

            return slnEditor.GetChangedSolution();
        }

        private async Task UpdateCallers(ISymbol methodSymbol, SolutionEditor slnEditor, CancellationToken token)
        {
            // Check callers
            var callers = await SymbolFinder.FindCallersAsync(methodSymbol, slnEditor.OriginalSolution, token);

            foreach (var caller in callers)
            {
                var location = caller.Locations.FirstOrDefault();

                if (location is null)
                {
                    continue;
                }

                if (!TryGetDocument(slnEditor.OriginalSolution, location.SourceTree, token, out var document))
                {
                    continue;
                }

                var editor = await slnEditor.GetDocumentEditorAsync(document.Id, token);
                var root = await document.GetSyntaxRootAsync(token);
                var httpContextCurrent = (ArgumentSyntax)editor.Generator.Argument(ParseExpression("HttpContext.Current"));
                var callerNode = root.FindNode(location.SourceSpan, getInnermostNodeForTie: true);

                if (callerNode is null)
                {
                    continue;
                }

                var invocationExpression = callerNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();

                if (invocationExpression is null)
                {
                    continue;
                }

                var argList = invocationExpression.ArgumentList.AddArguments(httpContextCurrent);

                editor.ReplaceNode(invocationExpression, invocationExpression.WithArgumentList(argList));
            }
        }

        private bool TryGetDocument(Solution sln, SyntaxTree? tree, CancellationToken token, out Document document)
        {
            if (tree is null)
            {
                document = null;
                return false;
            }

            foreach (var project in sln.Projects)
            {
                var doc = project.GetDocument(tree);

                if (doc is not null)
                {
                    document = doc;
                    return true;
                }
            }

            document = null;
            return false;
        }
    }
}
