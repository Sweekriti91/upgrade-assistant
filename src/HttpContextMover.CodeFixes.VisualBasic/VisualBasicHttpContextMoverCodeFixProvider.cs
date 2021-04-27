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

        protected override void ReplaceMethod(SyntaxNode callerNode, SyntaxEditor editor, IPropertySymbol property)
        {
            var invocationExpression = callerNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();

            if (invocationExpression is null)
            {
                return;
            }

            var httpContextType = editor.Generator.NameExpression(property.Type);
            var expression = editor.Generator.MemberAccessExpression(httpContextType, "Current");
            var httpContextCurrentArg = (ArgumentSyntax)editor.Generator.Argument(expression);
            var argList = invocationExpression.ArgumentList.AddArguments(httpContextCurrentArg);

            editor.ReplaceNode(invocationExpression, invocationExpression.WithArgumentList(argList));
        }
    }
}
