using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;

namespace HttpContextMover
{
    public class Mapping
    {
        public static ImmutableArray<Mapping> Default { get; } = ImmutableArray.Create(new Mapping[]
        {
            new Mapping("System.Web.HttpContext", "Current", "currentContext"),
        });

        public const string DefaultCurrentContextName = "DefaultCurrentContextName";

        private readonly string[] _typeName;
        private readonly string _propertyName;
        private readonly string _variableName;

        public Mapping(string typeName, string propertyName, string variableName)
        {
            _typeName = typeName.Split('.');
            _propertyName = propertyName;
            _variableName = variableName;

            var properties = ImmutableDictionary.CreateBuilder<string, string>();
            properties.Add(DefaultCurrentContextName, _variableName);
            Properties = properties.ToImmutable();
        }

        public ImmutableDictionary<string, string> Properties { get; }

        public static ImmutableArray<Mapping> Create(SourceText text)
        {
            var result = new Mapping[text.Length];
            var count = 0;

            foreach (var line in text.Lines)
            {
                var split = line.ToString().Split('\t');

                if (split.Length != 3)
                {
                    continue;
                }

                result[count++] = new Mapping(split[0], split[1], split[2]);
            }

            return ImmutableArray.Create(result, 0, count);
        }

        public bool Matches(IPropertySymbol property)
        {
            if (!property.Name.Equals(_propertyName, StringComparison.Ordinal))
            {
                return false;
            }

            return EqualsTypeParts(property.Type);
        }

        private bool EqualsTypeParts(ITypeSymbol typeSymbol)
        {
            var symbol = (INamespaceOrTypeSymbol)typeSymbol;

            for (int i = _typeName.Length - 1; i >= 0; i--)
            {
                if (!string.Equals(symbol.Name, _typeName[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                symbol = symbol.ContainingNamespace;
            }

            return symbol is INamespaceSymbol ns && ns.IsGlobalNamespace;
        }
    }
}
