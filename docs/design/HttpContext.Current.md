# HttpContext.Current Recommendations

`HttpContext.Current` is often used within codebases as a quick way to access the current context. This is often very low-down in the stack. When moving to ASP.NET Core, this API is not available and usage must be refactored. There are two main ways of addressing this that will be discussed below:

## Use HttpContextAccessor
A common way to address this is to replace usage of `HttpContext.Current` is with a similar construct to the following:

```csharp
namespace Microsoft.AspNetCore.Http
{
    /// <summary>
    /// Temporary helper class for retrieving the current HttpContext. This temporary
    /// workaround should be removed in the future and HttpContext should be retrieved
    /// from the current controller, middleware, or page instead. If working in another
    /// component, the current HttpContext can be retrieved from an IHttpContextAccessor
    /// retrieved via dependency injection.
    /// </summary>
    internal  static class HttpContextHelper
    {
        private static HttpContextAccessor HttpContextAccessor = new HttpContextAcessor();

        /// <summary>
        /// Gets the current HttpContext. Returns null if there is no current HttpContext.
        /// </summary>
        public static HttpContext Current => HttpContextAccessor.HttpContext;
    }
}
```

This has the following benefits:

- Relatively simple with minimal refactoring required

Problems with this approach:

- Retains web dependencies at potentially low levels of an application
- Potential perf issues with async-locals

## Refactor to inject HttpContext
Another way to address this is to inject HttpContext into the method or class. An example is given below:

```csharp
public string GetUserAgent() => HttpContext.Current.UserAgent;
```

could be converted to:

```csharp
public string GetUserAgent(HttpContext context) => context.UserAgent;
```

This has the following benefits:

- Removes any async-local usage
- Can more easily remove web dependencies from lower level

Problems with this approach:

- Can potentially be a large architectural change
