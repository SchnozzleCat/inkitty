using Inkitty.Core.FileSystem;

namespace Inkitty.Core.Projects;

/// <summary>
/// Project-relative paths are the canonical key for files: forward slashes, no <c>./</c>,
/// no <c>..</c>, relative to the folder containing the main ink file.
/// </summary>
public static class ProjectPaths
{
    /// <summary>
    /// Normalises an include name (as written in <c>INCLUDE</c>) or any relative path to the
    /// canonical form. Returns <c>null</c> if the path is empty or escapes the project root.
    /// </summary>
    public static string? Normalize(string rootDirectory, string path)
    {
        var trimmed = path.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var unified = trimmed.Replace('\\', '/');
        var full = Path.GetFullPath(Path.IsPathFullyQualified(unified) ? unified : Path.Join(rootDirectory, unified));
        var relative = Path.GetRelativePath(rootDirectory, full);

        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative))
        {
            return null;
        }

        return relative.Replace('\\', '/');
    }

    public static string ToFullPath(string rootDirectory, string relativePath) =>
        Path.GetFullPath(Path.Join(rootDirectory, relativePath));

    public static bool IsInkFile(string path) => path.EndsWith(".ink", PathComparer.Comparison);
}
