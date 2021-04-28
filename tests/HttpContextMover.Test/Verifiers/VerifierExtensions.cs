using Microsoft.CodeAnalysis.Testing;
using System.Linq;
using System;

namespace HttpContextMover.Test
{
    public static class VerifierExtensions
    {
        public static void AddMappings(this SourceFileCollection sources)
        {
            var mappings = new[]
            {
                new [] { "System.Web.HttpContext", "Current", "currentContext" }
            };

            var contents = string.Join(Environment.NewLine, mappings.Select(m => string.Join('\t', m)));
            sources.Add(("StaticDependencyInjection.mapping", contents));
        }
    }
}
