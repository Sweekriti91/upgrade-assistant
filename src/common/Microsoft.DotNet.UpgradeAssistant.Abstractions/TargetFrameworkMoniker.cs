// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Text;

namespace Microsoft.DotNet.UpgradeAssistant
{
    public record TargetFrameworkMoniker(string Framework, Version FrameworkVersion) : IEquatable<TargetFrameworkMoniker>
    {
        public static readonly TargetFrameworkMoniker NetStandard10 = new("netstandard", new Version(1, 0));

        public static readonly TargetFrameworkMoniker NetStandard20 = NetStandard10 with { FrameworkVersion = new Version(2, 0) };

        public static readonly TargetFrameworkMoniker NetStandard21 = NetStandard20 with { FrameworkVersion = new Version(2, 1) };

        public static readonly TargetFrameworkMoniker NetCoreApp21 = new("netcoreapp", new Version(2, 1));

        public static readonly TargetFrameworkMoniker NetCoreApp30 = NetCoreApp21 with { FrameworkVersion = new Version(3, 0) };

        public static readonly TargetFrameworkMoniker NetCoreApp31 = NetCoreApp30 with { FrameworkVersion = new Version(3, 1) };

        public static readonly TargetFrameworkMoniker Net45 = new("net", new Version(4, 5));

        public static readonly TargetFrameworkMoniker Net46 = Net45 with { FrameworkVersion = new Version(4, 6) };

        public static readonly TargetFrameworkMoniker Net461 = Net45 with { FrameworkVersion = new Version(4, 6, 1) };

        public static readonly TargetFrameworkMoniker Net462 = Net45 with { FrameworkVersion = new Version(4, 6, 2) };

        public static readonly TargetFrameworkMoniker Net47 = Net45 with { FrameworkVersion = new Version(4, 7) };

        public static readonly TargetFrameworkMoniker Net471 = Net45 with { FrameworkVersion = new Version(4, 7, 1) };

        public static readonly TargetFrameworkMoniker Net472 = Net45 with { FrameworkVersion = new Version(4, 7, 2) };

        public static readonly TargetFrameworkMoniker Net48 = Net45 with { FrameworkVersion = new Version(4, 8) };

        public static readonly TargetFrameworkMoniker Net50 = NetCoreApp30 with { Framework = "net", FrameworkVersion = new Version(5, 0) };

#pragma warning disable CA1707 // Identifiers should not contain underscores
        public static readonly TargetFrameworkMoniker Net50_Windows = Net50 with { Platform = Platforms.Windows };

        public static readonly TargetFrameworkMoniker Net60 = Net50 with { FrameworkVersion = new Version(6, 0) };

        public static readonly TargetFrameworkMoniker Net60_Windows = Net60 with { Platform = Platforms.Windows };
#pragma warning restore CA1707 // Identifiers should not contain underscores

#pragma warning disable CA1034 // Nested types should not be visible
        public static class Platforms
#pragma warning restore CA1034 // Nested types should not be visible
        {
            public const string Windows = "windows";
        }

        private const string NetStandardNamePrefix = "netstandard";
        private const string NetPrefix = "net";
        private const string NetCorePrefix = "netcoreapp";

        public string? Platform { get; init; }

        public Version? PlatformVersion { get; init; }

        public string Name => ToString();

        public TargetFrameworkMoniker Normalize()
            => this with
            {
                Framework = NormalizedFramework,
                FrameworkVersion = NormalizeVersion(FrameworkVersion) ?? FrameworkVersion,
                Platform = IsWindows ? Platforms.Windows : Platform,
                PlatformVersion = NormalizeVersion(PlatformVersion)
            };

        public virtual bool Equals(TargetFrameworkMoniker? obj)
        {
            if (obj is null)
            {
                return false;
            }

            var @this = Normalize();
            var tfm = obj.Normalize();

            return string.Equals(@this.Framework, tfm.Framework, StringComparison.OrdinalIgnoreCase)
                && Equals(@this.FrameworkVersion, tfm.FrameworkVersion)
                && string.Equals(@this.Platform, tfm.Platform, StringComparison.OrdinalIgnoreCase)
                && Equals(@this.PlatformVersion, tfm.PlatformVersion);
        }

        public override int GetHashCode()
        {
            var normalized = Normalize();
            var hashcode = default(HashCode);

            hashcode.Add(normalized.Framework, StringComparer.OrdinalIgnoreCase);
            hashcode.Add(normalized.FrameworkVersion);
            hashcode.Add(normalized.Platform, StringComparer.OrdinalIgnoreCase);
            hashcode.Add(normalized.PlatformVersion);

            return hashcode.ToHashCode();
        }

        private string NormalizedFramework
        {
            get
            {
                if (IsFramework || IsNet50OrAbove)
                {
                    return NetPrefix;
                }
                else if (IsNetCoreApp)
                {
                    return NetCorePrefix;
                }
                else if (IsNetStandard)
                {
                    return NetStandardNamePrefix;
                }
                else
                {
                    return Framework;
                }
            }
        }

        private Version? NormalizeVersion(Version? v)
            => v switch
            {
                null => null,
                (0, 0, 0, 0) => null,
                _ when v.Build > 0 && v.Revision > 0 => v,
                _ when v.Build > 0 => new Version(v.Major, v.Minor, v.Build),
                _ => new Version(v.Major, v.Minor),
            };

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (IsFramework)
            {
                sb.Append(NetPrefix);
                WriteVersionString(sb, FrameworkVersion, false);
            }
            else if (IsNet50OrAbove)
            {
                sb.Append(NetPrefix);
                WriteVersionString(sb, FrameworkVersion, true);

                if (Platform is not null)
                {
                    sb.Append('-');
                    sb.Append(Platform);

                    if (PlatformVersion is not null)
                    {
                        sb.Append(PlatformVersion);
                    }
                }
            }
            else if (IsNetCoreApp)
            {
                sb.Append(NetCorePrefix);
                WriteVersionString(sb, FrameworkVersion, true);
            }
            else if (IsNetStandard)
            {
                sb.Append(NetStandardNamePrefix);
                WriteVersionString(sb, FrameworkVersion, true);
            }

            return sb.ToString();
        }

        private void WriteVersionString(StringBuilder builder, Version version, bool includeDecimal)
        {
            var count = VersionCount(version);

            builder.Append(version.Major);
            AppendDecimal();

            builder.Append(version.Minor);

            if (count > 2)
            {
                AppendDecimal();
                builder.Append(version.Build);
            }

            if (count > 3)
            {
                AppendDecimal();
                builder.Append(version.Revision);
            }

            void AppendDecimal()
            {
                if (includeDecimal)
                {
                    builder.Append('.');
                }
            }

            static int VersionCount(Version v)
            {
                if (v.Revision > 0)
                {
                    return 4;
                }
                else if (v.Build > 0)
                {
                    return 3;
                }
                else
                {
                    return 2;
                }
            }
        }

        public bool IsFramework => Framework.Equals(NetPrefix, StringComparison.OrdinalIgnoreCase) && !Is50OrAbove;

        public bool IsNetStandard => Framework.ToUpperInvariant().Contains(NetStandardNamePrefix.ToUpperInvariant());

        private bool IsNet50OrAbove => (IsNetCoreApp || Framework.Equals(NetPrefix, StringComparison.OrdinalIgnoreCase)) && Is50OrAbove;

        private bool Is50OrAbove => FrameworkVersion.Major >= 5;

        private bool IsNetCoreApp => Framework.ToUpperInvariant().Contains(NetCorePrefix.ToUpperInvariant());

        public bool IsNetCore => IsNetCoreApp || IsNet50OrAbove;

        public bool IsWindows => Platform?.Equals(Platforms.Windows, StringComparison.OrdinalIgnoreCase) ?? false;
    }
}
