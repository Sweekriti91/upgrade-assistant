using Microsoft.CodeAnalysis;
using System;

namespace HttpContextMover
{
    internal static class DiagnosticAnalyzerExtensions
    {
        public static bool EqualsTypeParts(this ITypeSymbol symbol, params string[] parts)
            => ((INamespaceOrTypeSymbol)symbol).EqualsTypeParts(parts);

        public static bool EqualsTypeParts(this INamespaceOrTypeSymbol symbol, params string[] parts)
        {
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (!string.Equals(symbol.Name, parts[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                symbol = symbol.ContainingNamespace;
            }

            return symbol is null;
        }
    }
}
