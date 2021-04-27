using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.CodeAnalysis.Editing;
using System.Composition;
using Microsoft.CodeAnalysis.Operations;

namespace HttpContextMover
{
    [ExportCodeFixProvider(LanguageNames.VisualBasic, Name = nameof(VisualBasicHttpContextMoverCodeFixProvider)), Shared]
    public class VisualBasicHttpContextMoverCodeFixProvider : HttpContextMoverCodeFixProvider
    {
        protected override bool IsEnclosedMethodOperation(IOperation operation)
            // Methods and properties in VB resolve to an IBlockOperation. We only want methods and not properties so we also check for a MethodBlockSyntax
            => operation is IBlockOperation && operation.Syntax is MethodBlockSyntax;

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
