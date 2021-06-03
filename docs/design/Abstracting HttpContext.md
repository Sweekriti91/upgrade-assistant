# Abstracting HttpContext

Older codebases will often have `HttpContext` fairly low down in the stack and are often used to access properties such as:

- Headers
- Cookies
- Session
- etc

Some of these have more analogous counterparts between ASP.NET and ASP.NET Core than others. For instance, headers are available similarly between the two while session will be very different.

## Wrapping HttpContext
The first possible way is to create a wrapper of `HttpContext` that would be implemented for a `System.Web.HttpContext` and for `Microsoft.AspNetCore.Http.HttpContext`.  By using an interface here, the libraries lower in the stack do not need to know about where it comes from.

An example is:

```csharp
public interface ICustomContext
{
    string GetHeader(string name);
}

public class SystemWebCustomContext : ICustomContext
{
    private readonly System.Web.HttpContext _context;

    public SystemWebCustomContext(System.Web.HttpContext context)
    {
        _context = context;
    }

    public string GetHeader(string name) => _context.Request.Headers[name];
}

public class AspNetCoreCustomContext : ICustomContext
{
    private readonly Microsoft.AspNetCore.Http.HttpContext _context;

    public AspNetCoreCustomContext(Microsoft.AspNetCore.Http.HttpContext context)
    {
        _context = context;
    }

    public string GetHeader(string name) => _context.Request.Headers[name];
}
```

## Copy to POCO
Another way to address this is to create a plain-old CLR object (POCO) that contains the properties that matter. This has the benefit that once retrieved, it no longer requires the HttpContext anymore and won't potentially be used outside of a request.

## Code fixer
We could automate swapping HttpContext to a custom instance that uses either of these patterns. Then the user can use the `Add missing member` methods in VS to expand the custom types.