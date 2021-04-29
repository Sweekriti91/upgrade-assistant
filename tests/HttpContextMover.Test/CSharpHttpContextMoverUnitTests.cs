using Microsoft.CodeAnalysis.Testing;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

using VerifyCS = HttpContextMover.Test.CSharpCodeFixVerifier<
    HttpContextMover.HttpContextMoverAnalyzer,
    HttpContextMover.CSharpHttpContextMoverCodeFixProvider>;

namespace HttpContextMover.Test
{
    public class CSharpHttpContextMoverUnitTest
    {
        private const string HttpContextName = "HttpContext";
        private const string HttpContextCurrentName = "HttpContext HttpContext.Current";

        [Fact]
        public async Task EmptyCode()
        {
            var test = @"";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [Fact]
        public async Task SimpleUse()
        {
            var test = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public void Test()
            {
                _ = {|#0:HttpContext.Current|};
            }
        }
    }";
            var fixtest = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public void Test(HttpContext currentContext)
            {
                _ = currentContext;
            }
        }
    }";

            var expected = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task ExpressionBody()
        {
            var test = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public object Test() => {|#0:HttpContext.Current|};
        }
    }";
            var fixtest = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public object Test(HttpContext currentContext) => currentContext;
        }
    }";

            var expected = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task ReuseArgument()
        {
            var test = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public void Test(HttpContext currentContext)
            {
                _ = {|#0:HttpContext.Current|};
            }
        }
    }";
            var fixtest = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public void Test(HttpContext currentContext)
            {
                _ = currentContext;
            }
        }
    }";

            var expected = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task InArgument()
        {
            var test = @"
    using System.Web;

    namespace ConsoleApp1
    {
        public class Program
        {
            private static void Test(HttpContext currentContext)
            {
            }
            public static void Test2() => Test({|#0:HttpContext.Current|});
        }
    }";
            var fixtest = @"
    using System.Web;

    namespace ConsoleApp1
    {
        public class Program
        {
            private static void Test(HttpContext currentContext)
            {
            }
            public static void Test2(HttpContext currentContext) => Test(currentContext);
        }
    }";

            var expected = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task ReuseParameterName()
        {
            var test = @"
    using System.Web;

    namespace ConsoleApp1
    {
        public class Program
        {
            private static void Test()
            {
                Test2({|#0:HttpContext.Current|});
            }
            public static void Test2(HttpContext currentContext) => Test();
        }
    }";
            var fixtest = @"
    using System.Web;

    namespace ConsoleApp1
    {
        public class Program
        {
            private static void Test(HttpContext currentContext)
            {
                Test2(currentContext);
            }
            public static void Test2(HttpContext currentContext) => Test(currentContext);
        }
    }";

            var expected = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [Fact]
        public async Task ReplaceCallerInSameDocument()
        {
            var test = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public void Test()
            {
                _ = {|#0:HttpContext.Current|};
            }

            public void Test2()
            {
                Test();
            }
        }
    }";
            var fixtest = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public void Test(HttpContext currentContext)
            {
                _ = currentContext;
            }

            public void Test2()
            {
                Test({|#0:HttpContext.Current|});
            }
        }
    }";

            var expected1 = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);
            var expected2 = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            await VerifyCS.VerifyCodeFixAsync(test, expected1, fixtest, expected2);
        }

        [Fact]
        public async Task InProperty()
        {
            var test = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program
        {
            public object Instance => {|#0:HttpContext.Current|};
        }
    }";

            var expected1 = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            await VerifyCS.VerifyCodeFixAsync(test, expected1, test, expected1);
        }

        [Fact]
        public async Task MultipleFiles()
        {
            var test1 = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        public class Program
        {
            public static object Instance() => {|#0:HttpContext.Current|};
        }
    }";
            var test2 = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program2
        {
            public object Instance() => Program.Instance();
        }
    }";

            var fix1 = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        public class Program
        {
            public static object Instance(HttpContext currentContext) => currentContext;
        }
    }";
            var fix2 = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        class Program2
        {
            public object Instance() => Program.Instance({|#0:HttpContext.Current|});
        }
    }";
            var expected = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            var test = new VerifyCS.Test
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
        public async Task MultipleFilesNoSystemWebUsing()
        {
            var test1 = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        public class Program
        {
            public static object Instance() => {|#0:HttpContext.Current|};
        }
    }";
            var test2 = @"
    namespace ConsoleApplication1
    {
        class Program2
        {
            public object Instance() => Program.Instance();
        }
    }";

            var fix1 = @"
    using System.Web;

    namespace ConsoleApplication1
    {
        public class Program
        {
            public static object Instance(HttpContext currentContext) => currentContext;
        }
    }";
            var fix2 = @"
    namespace ConsoleApplication1
    {
        class Program2
        {
            public object Instance() => Program.Instance({|#0:System.Web.HttpContext.Current|});
        }
    }";
            var expected = VerifyCS.Diagnostic().WithLocation(0).WithArguments(HttpContextName, HttpContextCurrentName);

            var test = new VerifyCS.Test
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
