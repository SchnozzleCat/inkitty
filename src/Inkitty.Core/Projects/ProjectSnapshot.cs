using System.Collections.Frozen;
using Inkitty.Core.FileSystem;

namespace Inkitty.Core.Projects;

/// <summary>
/// Immutable copy of every buffer in a project at one point in time. Taken on the UI thread,
/// consumed by background compilation. Cheap: strings are immutable, so only references are copied.
/// </summary>
public sealed class ProjectSnapshot
{
    private readonly FrozenDictionary<string, string> _texts;
    private readonly FrozenDictionary<string, int> _versions;

    public ProjectSnapshot(string rootDirectory, string mainFile, IEnumerable<(string RelativePath, string Text, int Version)> files)
    {
        RootDirectory = rootDirectory;
        MainFile = mainFile;
        var list = files.ToList();
        _texts = list.ToFrozenDictionary(f => f.RelativePath, f => f.Text, PathComparer.Default);
        _versions = list.ToFrozenDictionary(f => f.RelativePath, f => f.Version, PathComparer.Default);
    }

    public string RootDirectory { get; }

    /// <summary>Relative path of the main (root) ink file.</summary>
    public string MainFile { get; }

    public string MainText => TextOf(MainFile) ?? string.Empty;

    public IEnumerable<string> Files => _texts.Keys;

    public string? TextOf(string relativePath) => _texts.GetValueOrDefault(relativePath);

    public int VersionOf(string relativePath) => _versions.GetValueOrDefault(relativePath, -1);

    /// <summary>
    /// Maps a file name as reported by ink (the include path as written, or the main file name)
    /// to a canonical relative path. Unknown names are normalised but may not exist in the snapshot.
    /// </summary>
    public string? ResolveFileName(string? inkFileName) =>
        inkFileName is null ? MainFile : ProjectPaths.Normalize(RootDirectory, inkFileName);
}
