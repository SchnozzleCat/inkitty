using System.Reflection;

namespace Inkitty.App;

/// <summary>
/// The version shown in the status bar. Release builds get it from their tag (CI passes
/// <c>-p:Version=0.1.0</c>); other builds are "dev", plus the commit they were built from when known.
/// </summary>
public static class AppVersion
{
    /// <summary>The version for display, e.g. "v0.1.0" or "dev 3216abd".</summary>
    public static string Display { get; } = Format(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Turns an informational version ("0.1.0+3216abd…", "0.0.0-dev+3216abd…") into <see cref="Display"/>'s form.</summary>
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "dev";
        }

        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        var version = plus < 0 ? informationalVersion : informationalVersion[..plus];
        var commit = plus < 0 ? null : informationalVersion[(plus + 1)..];
        if (version.StartsWith("0.0.0-dev", StringComparison.Ordinal))
        {
            return commit is { Length: > 0 } ? $"dev {commit[..Math.Min(7, commit.Length)]}" : "dev";
        }

        // A plain release number gets the usual "v"; anything else (e.g. a Nix "0-unstable-…") as given.
        return char.IsAsciiDigit(version[0]) && version.Contains('.', StringComparison.Ordinal) ? $"v{version}" : version;
    }
}
