using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;
using System.IO;

namespace HttpContextMover
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public class HttpContextMoverAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "HttpContextMover";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.HttpContext1Title), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.HttpContext1MessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.HttpContext1Description), Resources.ResourceManager, typeof(Resources));
        private const string Category = "Naming";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: Description);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(ctx =>
            {
                var mappings = GetMapping(ctx.Options.AdditionalFiles);

                if (mappings.IsDefaultOrEmpty)
                {
                    mappings = Mapping.Default;
                }

                ctx.RegisterOperationAction(ctx =>
                {
                    if (ctx.Operation is not IPropertyReferenceOperation propertyReference)
                    {
                        return;
                    }

                    foreach (var mapping in mappings)
                    {
                        if (mapping.Matches(propertyReference.Property))
                        {
                            var diagnostic = Diagnostic.Create(Rule, ctx.Operation.Syntax.GetLocation(), mapping.Properties);

                            ctx.ReportDiagnostic(diagnostic);
                        }
                    }
                }, OperationKind.PropertyReference);
            });
        }

        private static ImmutableArray<Mapping> GetMapping(ImmutableArray<AdditionalText> additionalFiles)
        {
            const string Name = "StaticDependencyInjection.mapping";

            foreach (var file in additionalFiles)
            {
                if (string.Equals(Path.GetFileName(file.Path), Name, StringComparison.OrdinalIgnoreCase))
                {
                    return Mapping.Create(file.GetText());
                }
            }

            return default;
        }
    }
}
