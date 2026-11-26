using System.IO.Compression;
using Inkitty.Core.FileSystem;
using Inkitty.Core.Projects;

namespace Inkitty.Core.Workspace;

/// <summary>Zips a project's ink files, and reads ink files back out of a zip.</summary>
public static class ProjectArchive
{
    /// <summary>
    /// Zips every <c>.ink</c> file under the project folder (relative paths, forward slashes).
    /// Loaded buffers win over disk, so unsaved edits and not-yet-saved INCLUDEd files are exported too.
    /// </summary>
    public static byte[] Create(InkProject project, IFileSystem? fileSystem = null)
    {
        fileSystem ??= PhysicalFileSystem.Instance;
        SortedDictionary<string, byte[]> entries = new(StringComparer.Ordinal);
        foreach (var path in fileSystem.EnumerateFiles(project.RootDirectory, "*.ink", recursive: true))
        {
            if (ProjectPaths.Normalize(project.RootDirectory, path) is { } relative && !relative.Split('/').Any(s => s.StartsWith('.')))
            {
                entries[relative] = fileSystem.ReadAllBytes(path);
            }
        }

        foreach (var file in project.Files)
        {
            entries[file.RelativePath] = TextFileCodec.Encode(file.Text, file.Format);
        }

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Reads the <c>.ink</c> files in a zip as (relative path, bytes). Entries that would escape the
    /// target folder, hidden files and non-ink files are skipped. If every file sits in one top-level
    /// folder (a zipped project folder), that folder is stripped.
    /// </summary>
    public static IReadOnlyList<(string Path, byte[] Contents)> ReadInkFiles(Stream zipStream)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        List<(string Path, byte[] Contents)> files = [];
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith('/') || !ProjectPaths.IsInkFile(name) || SafeRelativePath(name) is not { } safe)
            {
                continue;
            }

            using var stream = entry.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            files.Add((safe, copy.ToArray()));
        }

        var roots = files.Select(f => f.Path.Split('/')).ToList();
        if (roots.Count > 0 && roots.All(p => p.Length > 1) && roots.Select(p => p[0]).Distinct(StringComparer.Ordinal).Count() == 1)
        {
            files = [.. files.Select(f => (f.Path[(f.Path.IndexOf('/', StringComparison.Ordinal) + 1)..], f.Contents))];
        }

        return files;
    }

    /// <summary>Returns the path normalised to safe relative form, or null if it is absolute, escapes, or is hidden.</summary>
    public static string? SafeRelativePath(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal)
            || segments.Any(s => s is "." or ".." || s.StartsWith('.')))
        {
            return null;
        }

        return string.Join('/', segments);
    }
}
