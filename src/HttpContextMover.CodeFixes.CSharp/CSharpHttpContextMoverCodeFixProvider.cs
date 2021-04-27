using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Composition;

namespace HttpContextMover
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CSharpHttpContextMoverCodeFixProvider)), Shared]
    public class CSharpHttpContextMoverCodeFixProvider : HttpContextMoverCodeFixProvider<InvocationExpressionSyntax, ArgumentSyntax>
    {
        protected override bool IsEnclosedMethodOperation(IOperation operation)
            => operation is IMethodBodyOperation;
        protected override InvocationExpressionSyntax AddArgumentToInvocation(InvocationExpressionSyntax invocationNode, ArgumentSyntax argument)
            => invocationNode.WithArgumentList(invocationNode.ArgumentList.AddArguments(argument));
    }
}
