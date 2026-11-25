namespace Inkitty.Core.FileSystem;

/// <summary>
/// Path equality that matches the default file system of the current OS:
/// case-insensitive on Windows and macOS, case-sensitive elsewhere.
/// </summary>
public static class PathComparer
{
    public static StringComparer Default { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static bool PathEquals(string? a, string? b) => Default.Equals(a, b);
}
