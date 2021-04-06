// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.UpgradeAssistant;
using Microsoft.Extensions.Logging;

namespace Integration.Tests
{
    internal class KnownPackageLoader : IPackageLoader
    {
        private readonly KnownPackages _packages;
        private readonly IPackageLoader _other;
        private readonly ILogger<KnownPackageLoader> _logger;

        public KnownPackageLoader(KnownPackages packages, IPackageLoader other, ILogger<KnownPackageLoader> logger)
        {
            _packages = packages;
            _other = other;
            _logger = logger;
        }

        public IEnumerable<string> PackageSources => _other.PackageSources;

        public Task<bool> DoesPackageSupportTargetFrameworksAsync(NuGetReference packageReference, IEnumerable<TargetFrameworkMoniker> targetFrameworks, CancellationToken token)
        {
            return _other.DoesPackageSupportTargetFrameworksAsync(packageReference, targetFrameworks, token);
        }

        public async Task<NuGetReference?> GetLatestVersionAsync(string packageName, bool includePreRelease, string[]? packageSources, CancellationToken token)
        {
            if (_packages.TryGetValue(packageName, out var known))
            {
                return known;
            }

            var latest = await _other.GetLatestVersionAsync(packageName, includePreRelease, packageSources, token).ConfigureAwait(false);

            if (latest is not null)
            {
                _logger.LogError("Unexpected version: {Name}, {Version}", latest.Name, latest.Version);
            }

            return latest;
        }

        public Task<IEnumerable<NuGetReference>> GetNewerVersionsAsync(NuGetReference reference, bool latestMinorAndBuildOnly, CancellationToken token)
        {
            return _other.GetNewerVersionsAsync(reference, latestMinorAndBuildOnly, token);
        }
    }
}
