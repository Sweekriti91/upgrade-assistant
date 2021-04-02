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
            var node = root.FindNode(diagnosticSpan);
            var method = node.Parent.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().First();
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

            var editor = await DocumentEditor.CreateAsync(document, cancellationToken);

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

            // Check callers
            var callers = await SymbolFinder.FindCallersAsync(methodSymbol, document.Project.Solution, cancellationToken);
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var httpContextCurrent = (ArgumentSyntax)editor.Generator.Argument(ParseExpression("HttpContext.Current"));

            foreach (var caller in callers)
            {
                var location = caller.Locations.FirstOrDefault();

                if (location is null)
                {
                    continue;
                }

                var callerNode = root.FindNode(location.SourceSpan);

                if (callerNode is null)
                {
                    continue;
                }

                var invocationExpression = callerNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                var argList = invocationExpression.ArgumentList.AddArguments(httpContextCurrent);

                editor.ReplaceNode(invocationExpression, invocationExpression.WithArgumentList(argList));
            }

            return editor.GetChangedDocument().Project.Solution;
        }
    }
}
