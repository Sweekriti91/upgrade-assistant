using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

using VerifyVB = HttpContextMover.Test.VisualBasicCodeFixVerifier<
    HttpContextMover.HttpContextMoverAnalyzer,
    HttpContextMover.VisualBasicHttpContextMoverCodeFixProvider>;

namespace HttpContextMover.Test
{
    public class VbHttpContextMoverUnitTests
    {
        private const string HttpContextName = "HttpContext";
        private const string HttpContextCurrentName = "Property HttpContext.Current As HttpContext";

        [Fact]
        public async Task EmptyCode()
        {
            var test = @"";

            await VerifyVB.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task SimpleUse()
        {
            var test = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public  Sub Test()
                Dim c = {|#0:HttpContext.Current|}
            End Sub
        End Class
    End Namespace";
            var fixtest = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public  Sub Test(currentContext As HttpContext)
                Dim c = currentContext
        End Sub
        End Class
    End Namespace";

            var expected = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyVB.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task ReuseArgument()
        {
            var test = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public  Sub Test(currentContext As HttpContext)
                Dim c = {|#0:HttpContext.Current|}
            End Sub
        End Class
    End Namespace";
            var fixtest = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public  Sub Test(currentContext As HttpContext)
                Dim c = currentContext
        End Sub
        End Class
    End Namespace";

            var expected = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyVB.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task ReuseParameterName()
        {
            var test = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Sub Test()
                Test2({|#0:HttpContext.Current|})
            End Sub
            Public Sub Test2(currentContext As HttpContext)
                Test()
            End Sub
        End Class
    End Namespace";
            var fixtest = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Sub Test(currentContext As HttpContext)
                Test2(currentContext)
            End Sub
            Public Sub Test2(currentContext As HttpContext)
                Test(currentContext)
            End Sub
        End Class
    End Namespace";

            var expected = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyVB.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task InArgument()
        {
            var test = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Sub Test(currentContext As HttpContext)
            End Sub
            Public Function Test2() As HttpContext
                Return {|#0:HttpContext.Current|}
            End Function
        End Class
    End Namespace";
            var fixtest = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Sub Test(currentContext As HttpContext)
            End Sub
            Public Function Test2(currentContext As HttpContext) As HttpContext
                Return currentContext
        End Function
        End Class
    End Namespace";

            var expected = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyVB.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task ReplaceCallerInSameDocument()
        {
            var test = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Sub Test()
                Dim c = {|#0:HttpContext.Current|}
            End Sub
            Public Sub Test2()
                Test()
            End Sub
        End Class
    End Namespace";
            var fixtest = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Sub Test(currentContext As HttpContext)
                Dim c = currentContext
        End Sub
            Public Sub Test2()
                Test({|#0:HttpContext.Current|})
            End Sub
        End Class
    End Namespace";

            var expected1 = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            var expected2 = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            await VerifyVB.VerifyCodeFixAsync(test, expected1, fixtest, expected2);
        }

        [Fact]
        public async Task InProperty()
        {
            var test = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public ReadOnly Property Test As HttpContext
                Get
                    Return {|#0:HttpContext.Current|}
            End Get
            End Property
        End Class
    End Namespace";

            var expected1 = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            await VerifyVB.VerifyCodeFixAsync(test, expected1, test, expected1);
        }

        [Fact]
        public async Task MultipleFiles()
        {
            var test1 = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Shared Function Instance() As HttpContext
                Return {|#0:HttpContext.Current|}
            End Function
        End Class
    End Namespace";
            var test2 = @"
    Imports System.Web
    Namespace ConsoleApplication1
        Class Program2
            Public Function Test2() As HttpContext
                Return Program.Instance()
            End Function
        End Class
    End Namespace";

            var fix1 = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Shared Function Instance(currentContext As HttpContext) As HttpContext
                Return currentContext
        End Function
        End Class
    End Namespace";
            var fix2 = @"
    Imports System.Web
    Namespace ConsoleApplication1
        Class Program2
            Public Function Test2() As HttpContext
                Return Program.Instance({|#0:HttpContext.Current|})
            End Function
        End Class
    End Namespace";
            var expected = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            var test = new VerifyVB.Test
            {
                ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net45.Default.AddAssemblies(ImmutableArray.Create("System.Web")),
                CodeFixTestBehaviors = CodeFixTestBehaviors.FixOne,
            };

            test.ExpectedDiagnostics.Add(expected);

            test.TestState.Sources.Add(test1);
            test.TestState.Sources.Add(test2);
            test.TestState.AdditionalFiles.AddMappings();

            test.FixedState.Sources.Add(fix1);
            test.FixedState.Sources.Add(fix2);
            test.FixedState.ExpectedDiagnostics.Add(expected);

            await test.RunAsync(CancellationToken.None);
        }

        [Fact]
        public async Task MultipleFilesNoSystemWebImport()
        {
            var test1 = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Shared Function Instance() As Object
                Return {|#0:HttpContext.Current|}
            End Function
        End Class
    End Namespace";
            var test2 = @"
    Namespace ConsoleApplication1
        Class Program2
            Public Function Test2() As Object
                Return Program.Instance()
            End Function
        End Class
    End Namespace";

            var fix1 = @"
    Imports System.Web

    Namespace ConsoleApplication1
        Class Program
            Public Shared Function Instance(currentContext As HttpContext) As Object
                Return currentContext
        End Function
        End Class
    End Namespace";
            var fix2 = @"
    Namespace ConsoleApplication1
        Class Program2
            Public Function Test2() As Object
                Return Program.Instance({|#0:System.Web.HttpContext.Current|})
            End Function
        End Class
    End Namespace";
            var expected = VerifyVB.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            var test = new VerifyVB.Test
            {
                ReferenceAssemblies = ReferenceAssemblies.NetFramework.Net45.Default.AddAssemblies(ImmutableArray.Create("System.Web")),
                CodeFixTestBehaviors = CodeFixTestBehaviors.FixOne,
            };

            test.ExpectedDiagnostics.Add(expected);

            test.TestState.Sources.Add(test1);
            test.TestState.Sources.Add(test2);
            test.TestState.AdditionalFiles.AddMappings();

            test.FixedState.Sources.Add(fix1);
            test.FixedState.Sources.Add(fix2);
            test.FixedState.ExpectedDiagnostics.Add(expected);

            //We use a generator that ends up not creating the same syntax
            test.CodeActionValidationMode = CodeActionValidationMode.None;

            await test.RunAsync(CancellationToken.None);
        }
    }
}
