using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System.Composition;

namespace HttpContextMover
{
    [ExportCodeFixProvider(LanguageNames.VisualBasic, Name = nameof(VisualBasicHttpContextMoverCodeFixProvider)), Shared]
    public class VisualBasicHttpContextMoverCodeFixProvider : HttpContextMoverCodeFixProvider<InvocationExpressionSyntax, ArgumentSyntax>
    {
        protected override bool IsEnclosedMethodOperation(IOperation operation)
            // Methods and properties in VB resolve to an IBlockOperation. We only want methods and not properties so we also check for a MethodBlockSyntax
            => operation is IBlockOperation && operation.Syntax is MethodBlockSyntax;

        protected override InvocationExpressionSyntax AddArgumentToInvocation(InvocationExpressionSyntax invocationNode, ArgumentSyntax argument)
            => invocationNode.WithArgumentList(invocationNode.ArgumentList.AddArguments(argument));
    }
}
