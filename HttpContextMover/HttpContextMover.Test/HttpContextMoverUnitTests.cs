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
        //No diagnostics expected to show up
        [TestMethod]
        public async Task TestMethod1()
        {
            var test = @"";

            await VerifyCS.VerifyAnalyzerAsync(test);
        }

        //Diagnostic and CodeFix both triggered and checked for
        [TestMethod]
        public async Task TestMethod2()
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
    }
}
