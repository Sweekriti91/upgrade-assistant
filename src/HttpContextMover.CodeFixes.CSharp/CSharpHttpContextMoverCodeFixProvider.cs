using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Composition;

namespace HttpContextMover
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CSharpHttpContextMoverCodeFixProvider)), Shared]
    public class CSharpHttpContextMoverCodeFixProvider : HttpContextMoverCodeFixProvider
    {
        protected override bool IsEnclosedMethodOperation(IOperation operation)
            => operation is IMethodBodyOperation;

        protected override void ReplaceMethod(SemanticModel semanticModel, SyntaxNode callerNode, SyntaxEditor editor, IPropertySymbol property)
        {
            var invocationExpression = callerNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (invocationExpression is null)
            {
                return;
            }

            var newArg = (ArgumentSyntax)GetParameter(semanticModel, property, editor, invocationExpression, default);
            var argList = invocationExpression.ArgumentList.AddArguments(newArg);

            editor.ReplaceNode(invocationExpression, invocationExpression.WithArgumentList(argList));
        }
    }
}
