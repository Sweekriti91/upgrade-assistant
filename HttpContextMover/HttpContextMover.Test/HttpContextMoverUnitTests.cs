using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using VerifyCS = HttpContextMover.Test.CSharpCodeFixVerifier<
    HttpContextMover.HttpContextMoverAnalyzer,
    HttpContextMover.HttpContextMoverCodeFixProvider>;

namespace HttpContextMover.Test
{
    [TestClass]
    public class HttpContextMoverUnitTest
    {
        [TestMethod]
        public async Task EmptyCode()
        {
            var test = @"";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        [TestMethod]
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

            var expected = VerifyCS.Diagnostic("HttpContextMover").WithLocation(0).WithArguments("System.Web.HttpContext.Current");
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [TestMethod]
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

            var expected = VerifyCS.Diagnostic("HttpContextMover").WithLocation(0).WithArguments("System.Web.HttpContext.Current");
            await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
        }

        [TestMethod]
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

            var expected1 = VerifyCS.Diagnostic("HttpContextMover").WithLocation(0).WithArguments("System.Web.HttpContext.Current");
            var expected2 = VerifyCS.Diagnostic().WithLocation(0).WithArguments("System.Web.HttpContext.Current");

            await VerifyCS.VerifyCodeFixAsync(test, expected1, fixtest, expected2);
        }

        [TestMethod]
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

            var expected1 = VerifyCS.Diagnostic("HttpContextMover").WithLocation(0).WithArguments("System.Web.HttpContext.Current");

            await VerifyCS.VerifyCodeFixAsync(test, expected1, test, expected1);
        }
    }
}
