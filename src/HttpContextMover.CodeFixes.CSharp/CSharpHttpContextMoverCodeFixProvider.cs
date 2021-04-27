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
        protected override bool OperationApplies(IOperation operation)
            => operation is IMethodBodyOperation;

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
