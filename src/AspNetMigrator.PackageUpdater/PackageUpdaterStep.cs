﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace AspNetMigrator.PackageUpdater
{
    /// <summary>
    /// Migration step that updates NuGet package references
    /// to better work after migration. Packages references are
    /// updated if the reference appears to be transitive (with
    /// SDK style projects, only top-level dependencies are necessary
    /// in the project file), if the package version doesn't
    /// target a compatible .NET framework but a newer version does,
    /// or if the package is explicitly mapped to an updated
    /// NuGet package in a mapping configuration file.
    /// </summary>
    public class PackageUpdaterStep : MigrationStep
    {
        private const string AnalyzerPackageName = "AspNetMigrator.Analyzers";
        private const string PackageReferenceType = "PackageReference";
        private const string PackageMapExtension = "*.json";

        private readonly string? _analyzerPackageSource;
        private readonly string? _analyzerPackageVersion;
        private readonly IPackageLoader _packageLoader;
        private readonly IPackageRestorer _packageRestorer;
        private readonly string _packageMapSearchPath;
        private readonly bool _logRestoreOutput;
        private readonly NuGetFramework _targetFramework;
        private List<NuGetReference> _packagesToRemove;
        private List<NuGetReference> _packagesToAdd;

        public PackageUpdaterStep(MigrateOptions options, IPackageLoader packageLoader, IPackageRestorer packageRestorer, IOptions<PackageUpdaterStepOptions> updaterOptions, ILogger<PackageUpdaterStep> logger)
            : base(options, logger)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (updaterOptions is null)
            {
                throw new ArgumentNullException(nameof(updaterOptions));
            }

            if (logger is null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            Title = $"Update NuGet packages";
            Description = $"Update package references in {options.ProjectPath} to versions compatible with the target framework";
            _packageLoader = packageLoader ?? throw new ArgumentNullException(nameof(packageLoader));
            _packageRestorer = packageRestorer ?? throw new ArgumentNullException(nameof(packageRestorer));
            _packageMapSearchPath = Path.IsPathFullyQualified(updaterOptions.Value.PackageMapPath ?? string.Empty)
                ? updaterOptions.Value.PackageMapPath!
                : Path.Combine(AppContext.BaseDirectory, updaterOptions.Value.PackageMapPath ?? string.Empty);
            _analyzerPackageSource = updaterOptions.Value.MigrationAnalyzersPackageSource;
            _analyzerPackageVersion = updaterOptions.Value.MigrationAnalyzersPackageVersion;
            _logRestoreOutput = updaterOptions.Value.LogRestoreOutput;
            _targetFramework = NuGetFramework.Parse(options.TargetFramework);
            _packagesToRemove = new List<NuGetReference>();
            _packagesToAdd = new List<NuGetReference>();
        }

        protected override async Task<(MigrationStepStatus Status, string StatusDetails)> InitializeImplAsync(IMigrationContext context, CancellationToken token)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _packagesToRemove = new List<NuGetReference>();
            _packagesToAdd = new List<NuGetReference>();

            // Read package maps
            var packageMaps = await LoadPackageMapsAsync(token).ConfigureAwait(false);

            // Restore packages (to produce lockfile)
            var restoreOutput = await _packageRestorer.RestorePackagesAsync(_logRestoreOutput, context, token).ConfigureAwait(false);
            if (restoreOutput.LockFilePath is null)
            {
                var path = await context.GetProjectPathAsync(token).ConfigureAwait(false);
                Logger.LogCritical("Unable to restore packages for project {ProjectPath}", path);
                return (MigrationStepStatus.Failed, $"Unable to restore packages for project {path}");
            }

            // Parse lockfile
            var lockFile = LockFileUtilities.GetLockFile(restoreOutput.LockFilePath, NuGet.Common.NullLogger.Instance);
            var lockFileTarget = lockFile.Targets.First(t => t.TargetFramework.DotNetFrameworkName.Equals(_targetFramework.DotNetFrameworkName, StringComparison.Ordinal));

            try
            {
                // Iterate through all package references in the project file
                // TODO : Parallelize
                var projectRoot = await context.GetProjectRootElementAsync(token).ConfigureAwait(false);
                var packageReferences = projectRoot.GetAllPackageReferences();
                foreach (var reference in packageReferences)
                {
                    var packageReference = reference.AsNuGetReference();

                    // If the package is referenced more than once (bizarrely, this happens one of our test inputs), only keep the highest version
                    var highestVersion = packageReferences
                        .Where(r => r.Include.Equals(packageReference.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(r => r.AsNuGetReference().GetNuGetVersion())
                        .Max();
                    if (highestVersion > packageReference.GetNuGetVersion())
                    {
                        Logger.LogInformation("Marking package {NuGetPackage} for removal because it is referenced elsewhere in the project with a higher version", packageReference);
                        _packagesToRemove.Add(packageReference);
                        continue;
                    }

                    // If the package is referenced transitively, mark for removal
                    if (lockFileTarget.Libraries.Any(l => l.Dependencies.Any(d => ReferenceSatisfiesDependency(d, packageReference, true))))
                    {
                        Logger.LogInformation("Marking package {PackageName} for removal because it appears to be a transitive dependency", packageReference.Name);
                        _packagesToRemove.Add(packageReference);
                        continue;
                    }

                    // If the package is in a package map, mark for removal and add appropriate packages for addition
                    var map = packageMaps.FirstOrDefault(m => m.ContainsReference(packageReference.Name, packageReference.Version));
                    if (map != null)
                    {
                        Logger.LogInformation("Marking package {PackageName} for removal based on package mapping configuration", packageReference.Name);
                        _packagesToRemove.Add(packageReference);
                        _packagesToAdd.AddRange(map.NetCorePackages);
                        continue;
                    }

                    // If the package doesn't target the right framework but a newer version does, mark it for removal and the newer version for addition
                    if (await DoesPackageSupportTargetFrameworkAsync(packageReference, restoreOutput.PackageCachePath, token).ConfigureAwait(false))
                    {
                        Logger.LogDebug("Package {NuGetPackage} will work on {TargetFramework}", packageReference, _targetFramework);
                        continue;
                    }
                    else
                    {
                        // If the package won't work on the target Framework, check newer versions of the package
                        var updatedReference = await GetUpdatedPackageVersionAsync(packageReference, restoreOutput.PackageCachePath, token).ConfigureAwait(false);
                        if (updatedReference == null)
                        {
                            Logger.LogWarning("No version of {PackageName} found that supports {TargetFramework}; leaving unchanged", packageReference.Name, _targetFramework);
                        }
                        else
                        {
                            Logger.LogInformation("Marking package {NuGetPackage} for removal because it doesn't support the target framework but a newer version ({Version}) does", packageReference, updatedReference.Version);
                            _packagesToRemove.Add(packageReference);
                            _packagesToAdd.Add(updatedReference);
                            continue;
                        }
                    }
                }

                // If the project doesn't include a reference to the analyzer package, mark it for addition
                if (!packageReferences.Any(r => AnalyzerPackageName.Equals(r.Include, StringComparison.OrdinalIgnoreCase)))
                {
                    // Use the analyzer package version from configuration if specified, otherwise get the latest version.
                    // When looking for the latest analyzer version, use the analyzer package source from configuration
                    // if one is specified, otherwise just use the package sources from the project being analyzed.
                    var analyzerPackageVersion = _analyzerPackageVersion is not null
                        ? NuGetVersion.Parse(_analyzerPackageVersion)
                        : await _packageLoader.GetLatestVersionAsync(AnalyzerPackageName, true, _analyzerPackageSource is null ? null : new[] { _analyzerPackageSource }, token).ConfigureAwait(false);
                    if (analyzerPackageVersion is not null)
                    {
                        Logger.LogInformation("Reference to analyzer package ({AnalyzerPackageName}, version {AnalyzerPackageVersion}) needs added", AnalyzerPackageName, analyzerPackageVersion);
                        _packagesToAdd.Add(new NuGetReference(AnalyzerPackageName, analyzerPackageVersion.ToString()));
                    }
                    else
                    {
                        Logger.LogWarning("Analyzer NuGet package reference cannot be added because the package cannot be found");
                    }
                }
                else
                {
                    Logger.LogDebug("Reference to analyzer package ({AnalyzerPackageName}) already exists", AnalyzerPackageName);
                }
            }
            catch (InvalidProjectFileException)
            {
                Logger.LogCritical("Invalid project: {ProjectPath}", Options.ProjectPath);
                return (MigrationStepStatus.Failed, $"Invalid project: {Options.ProjectPath}");
            }

            _packagesToAdd = _packagesToAdd.Distinct().ToList();

            if (_packagesToRemove.Count == 0 && _packagesToAdd.Count == 0)
            {
                Logger.LogInformation("No package updates needed");
                return (MigrationStepStatus.Complete, "No package updates needed");
            }
            else
            {
                if (_packagesToRemove.Count > 0)
                {
                    Logger.LogInformation($"Packages to be removed:\n{string.Join('\n', _packagesToRemove)}");
                }

                if (_packagesToAdd.Count > 0)
                {
                    Logger.LogInformation($"Packages to be addded:\n{string.Join('\n', _packagesToAdd)}");
                }

                return (MigrationStepStatus.Incomplete, $"{_packagesToRemove.Count} packages need removed and {_packagesToAdd.Count} packages need added");
            }
        }

        protected override async Task<(MigrationStepStatus Status, string StatusDetails)> ApplyImplAsync(IMigrationContext context, CancellationToken token)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            var projectRoot = await context.GetProjectRootElementAsync(token).ConfigureAwait(false);

            // TODO : Temporary workaround until the migration analyzers are available on NuGet.org
            // Check whether the analyzer package's source is present in NuGet.config and add it if it isn't
            if (_analyzerPackageSource is not null && !_packageLoader.PackageSources.Contains(_analyzerPackageSource))
            {
                // Get or create a local NuGet.config file
                var localNuGetSettings = new Settings(projectRoot.DirectoryPath);

                // Add the analyzer package's source to the config file's sources
                localNuGetSettings.AddOrUpdate("packageSources", new SourceItem("migrationAnalyzerSource", _analyzerPackageSource));
                localNuGetSettings.SaveToDisk();
            }

            try
            {
                // Check each reference to see if it's one that should be removed
                foreach (var referenceItem in projectRoot.GetAllPackageReferences())
                {
                    var reference = referenceItem.AsNuGetReference();
                    if (_packagesToRemove.Contains(reference))
                    {
                        Logger.LogInformation("Removing outdated packaged reference: {PackageReference}", reference);
                        referenceItem.RemoveElement();
                    }
                }

                // Find a place to add new package references
                var packageReferenceItemGroup = projectRoot.ItemGroups.FirstOrDefault(g => g.Items.Any(i => i.ItemType.Equals(PackageReferenceType, StringComparison.OrdinalIgnoreCase)));
                if (packageReferenceItemGroup is null)
                {
                    Logger.LogDebug("Creating a new ItemGroup for package references");
                    packageReferenceItemGroup = projectRoot.CreateItemGroupElement();
                    projectRoot.AppendChild(packageReferenceItemGroup);
                }
                else
                {
                    Logger.LogDebug("Found ItemGroup for package references");
                }

                // Add replacement packages
                foreach (var newReference in _packagesToAdd.Distinct())
                {
                    Logger.LogInformation("Adding package reference to: {PackageReference}", newReference);
                    projectRoot.AddPackageReference(packageReferenceItemGroup, newReference);
                }

                projectRoot.Save();

                // Reload the workspace since, at this point, the project may be different from what was loaded
                await context.ReloadWorkspaceAsync(token).ConfigureAwait(false);

                return (MigrationStepStatus.Complete, "Packages updated");
            }
            catch (InvalidProjectFileException)
            {
                Logger.LogCritical("Invalid project: {ProjectPath}", Options.ProjectPath);
                return (MigrationStepStatus.Failed, $"Invalid project: {Options.ProjectPath}");
            }
        }

        private async Task<NuGetReference?> GetUpdatedPackageVersionAsync(NuGetReference packageReference, string? packageCachePath, CancellationToken token)
        {
            var latestMinorVersions = await _packageLoader.GetNewerVersionsAsync(packageReference.Name, new NuGetVersion(packageReference.GetNuGetVersion()), true, token).ConfigureAwait(false);
            NuGetReference? updatedReference = null;
            foreach (var newerPackage in latestMinorVersions.Select(v => new NuGetReference(packageReference.Name, v.ToString())))
            {
                if (await DoesPackageSupportTargetFrameworkAsync(newerPackage, packageCachePath, token).ConfigureAwait(false))
                {
                    Logger.LogDebug("Package {NuGetPackage} will work on {TargetFramework}", newerPackage, _targetFramework);
                    updatedReference = newerPackage;
                    break;
                }
            }

            return updatedReference;
        }

        private async Task<IEnumerable<NuGetFramework>> GetTargetFrameworksAsync(PackageArchiveReader packageArchive, CancellationToken token)
        {
            var frameworksNames = new List<NuGetFramework>();

            // Add any target framework there are libraries for
            var libraries = await packageArchive.GetLibItemsAsync(token).ConfigureAwait(false);
            frameworksNames.AddRange(libraries.Select(l => l.TargetFramework));

            // Add any target framework there are dependencies for
            var dependencies = await packageArchive.GetPackageDependenciesAsync(token).ConfigureAwait(false);
            frameworksNames.AddRange(dependencies.Select(d => d.TargetFramework));

            // Add any target framework there are reference assemblies for
            var refs = await packageArchive.GetReferenceItemsAsync(token).ConfigureAwait(false);
            frameworksNames.AddRange(refs.Select(r => r.TargetFramework));

            var ret = frameworksNames.Distinct();
            Logger.LogDebug("Found target frameworks for package {NuGetPackage}: {TargetFrameworks}", (await packageArchive.GetIdentityAsync(token).ConfigureAwait(false)).ToString(), ret);
            return ret;
        }

        private async Task<List<NuGetPackageMap>> LoadPackageMapsAsync(CancellationToken token)
        {
            var maps = new List<NuGetPackageMap>();

            if (Directory.Exists(_packageMapSearchPath))
            {
                var mapPaths = Directory.GetFiles(_packageMapSearchPath, PackageMapExtension, SearchOption.AllDirectories);

                foreach (var mapPath in mapPaths)
                {
                    token.ThrowIfCancellationRequested();

                    try
                    {
                        using var config = File.OpenRead(mapPath);
                        var newMaps = await JsonSerializer.DeserializeAsync<IEnumerable<NuGetPackageMap>>(config, cancellationToken: token).ConfigureAwait(false);
                        if (newMaps != null)
                        {
                            maps.AddRange(newMaps);
                            Logger.LogDebug("Loaded {MapCount} package maps from {PackageMapPath}", newMaps.Count(), mapPath);
                        }
                    }
                    catch (JsonException exc)
                    {
                        Logger.LogDebug(exc, "File {PackageMapPath} is not a valid package map file", mapPath);
                    }
                }

                Logger.LogDebug("Loaded {MapCount} package maps", maps.Count);
            }
            else
            {
                Logger.LogError("Package map search path ({PackageMapSearchPath}) not found", _packageMapSearchPath);
                throw new InvalidOperationException($"Package map search path ({_packageMapSearchPath}) not found");
            }

            return maps;
        }

        private async Task<bool> DoesPackageSupportTargetFrameworkAsync(NuGetReference packageReference, string? cachePath, CancellationToken token)
        {
            using var packageArchive = await _packageLoader.GetPackageArchiveAsync(packageReference, token, cachePath).ConfigureAwait(false);

            if (packageArchive is null)
            {
                return false;
            }

            var packageFrameworks = await GetTargetFrameworksAsync(packageArchive, token).ConfigureAwait(false);
            return packageFrameworks.Any(f => DefaultCompatibilityProvider.Instance.IsCompatible(_targetFramework, f));
        }

        private static bool ReferenceSatisfiesDependency(PackageDependency dependency, NuGetReference packageReference, bool minVersionMatchOnly)
        {
            // If the dependency's name doesn't match the reference's name, return false
            if (!dependency.Id.Equals(packageReference.Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var packageVersion = packageReference.GetNuGetVersion();
            if (packageVersion == null)
            {
                throw new InvalidOperationException("Package references from a lock file should always have a specific version");
            }

            // Return false if the reference's version falls outside of the dependency range
            var versionRange = dependency.VersionRange;
            if (versionRange.HasLowerBound && packageVersion < versionRange.MinVersion)
            {
                return false;
            }

            if (versionRange.HasUpperBound && packageVersion > versionRange.MaxVersion)
            {
                return false;
            }

            // In some cases (looking for transitive dependencies), it's interesting to only match packages that are the minimum version
            if (minVersionMatchOnly && versionRange.HasLowerBound && packageVersion != versionRange.MinVersion)
            {
                return false;
            }

            // Otherwise, return true
            return true;
        }
    }
}