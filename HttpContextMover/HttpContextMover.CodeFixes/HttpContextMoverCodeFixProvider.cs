using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

            if (root is null)
            {
                return;
            }

            // TODO: Replace the following code with your own analysis, generating a CodeAction for each fix to suggest
            var diagnostic = context.Diagnostics.First();
            var diagnosticSpan = diagnostic.Location.SourceSpan;

            //// Find the type declaration identified by the diagnostic.
            var node = root.FindNode(diagnosticSpan, getInnermostNodeForTie: true);
            var method = node.Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();

            if (method is null)
            {
                return;
            }

            var semantic = await context.Document.GetSemanticModelAsync(context.CancellationToken);

            if (semantic is null)
            {
                return;
            }

            var symbol = semantic.GetSymbolInfo(node);

            if (symbol.Symbol is not IPropertySymbol property)
            {
                return;
            }

            //// Register a code action that will invoke the fix.
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: CodeFixResources.HttpContextPassthroughCodeFixer,
                    createChangedSolution: c => MakePassHttpContextThrough(context.Document, property, node, method, c),
                    equivalenceKey: nameof(CodeFixResources.HttpContextPassthroughCodeFixer)),
                diagnostic);
        }

        private async Task<Solution> MakePassHttpContextThrough(Document document, IPropertySymbol property, SyntaxNode node, MethodDeclarationSyntax methodDecl, CancellationToken cancellationToken)
        {
            // Get the symbol representing the type to be renamed.
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);

            if (semanticModel is null)
            {
                return document.Project.Solution;
            }

            var slnEditor = new SolutionEditor(document.Project.Solution);
            var editor = await slnEditor.GetDocumentEditorAsync(document.Id, cancellationToken);

            // Add parameter if not available
            var parameter = methodDecl.ParameterList.Parameters.FirstOrDefault(p =>
            {
                if (p.Type is null)
                {
                    return false;
                }

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

            if (semanticModel.GetDeclaredSymbol(methodDecl, cancellationToken) is ISymbol methodSymbol)
            {
                await UpdateCallers(methodSymbol, property, slnEditor, cancellationToken);
            }

            return slnEditor.GetChangedSolution();
        }

        private async Task UpdateCallers(ISymbol methodSymbol, IPropertySymbol property, SolutionEditor slnEditor, CancellationToken token)
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

                if (root is null)
                {
                    continue;
                }

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

                var httpContextType = editor.Generator.NameExpression(property.Type);
                var expression = editor.Generator.MemberAccessExpression(httpContextType, "Current");
                var httpContextCurrentArg = (ArgumentSyntax)editor.Generator.Argument(expression);
                var argList = invocationExpression.ArgumentList.AddArguments(httpContextCurrentArg);

                editor.ReplaceNode(invocationExpression, invocationExpression.WithArgumentList(argList));
            }
        }

        private bool TryGetDocument(Solution sln, SyntaxTree? tree, CancellationToken token, [MaybeNullWhen(false)] out Document document)
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
