// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.DotNet.UpgradeAssistant;

namespace Integration.Tests
{
    internal class KnownPackages : IDisposable
    {
        private readonly Dictionary<string, NuGetReference?>? _knownValues;

        public KnownPackages()
        {
            var knownVersionsJson = File.ReadAllText("ExpectedPackageVersions.json");

            _unknown=new HashSet<>
            _knownValues = JsonSerializer.Deserialize<NuGetReference[]>(knownVersionsJson)
                ?.ToDictionary(r => r.Name)!;
        }

        public void Dispose()
        {
        }

        public bool TryGetValue(string name, [MaybeNullWhen(true)] out NuGetReference nuget)
        {
            if (_knownValues is null)
            {
                nuget = null;
                return false;
            }

            if(!_knownValues.TryGetValue(name, out nuget))
            {
                return false;
            }

            return true;
        }
    }
}
