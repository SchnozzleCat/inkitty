using Inkitty.Core.FileSystem;
using Inkitty.Core.Projects;

namespace Inkitty.Core.Workspace;

/// <summary>A project stored in a <see cref="ProjectWorkspace"/>.</summary>
/// <param name="Name">Folder name, shown to the user.</param>
/// <param name="MainFilePath">Full path of the project's main ink file.</param>
/// <param name="LastModified">Newest write time of any ink file in the project.</param>
/// <param name="FileCount">Number of ink files.</param>
public sealed record WorkspaceProject(string Name, string MainFilePath, DateTimeOffset LastModified, int FileCount);

/// <summary>
/// A folder of projects, one subfolder each, used where there is no user-visible file system
/// (the browser, where the folder lives in IndexedDB). Each project's main file is
/// <c>main.ink</c>, or its only top-level ink file.
/// </summary>
public sealed class ProjectWorkspace(string rootDirectory, IFileSystem? fileSystem = null)
{
    public const string MainFileName = "main.ink";

    private readonly IFileSystem _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;

    public string RootDirectory { get; } = rootDirectory;

    /// <summary>Projects, most recently modified first.</summary>
    public IReadOnlyList<WorkspaceProject> List()
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        List<WorkspaceProject> projects = [];
        foreach (var folder in Directory.EnumerateDirectories(RootDirectory))
        {
            var name = Path.GetFileName(folder);
            if (name.StartsWith('.') || FindMainFile(folder) is not { } main)
            {
                continue;
            }

            var files = _fileSystem.EnumerateFiles(folder, "*.ink", recursive: true).ToList();
            var modified = files.Select(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero)).DefaultIfEmpty().Max();
            projects.Add(new WorkspaceProject(name, main, modified, files.Count));
        }

        return [.. projects.OrderByDescending(p => p.LastModified).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The project a path belongs to, or null if it's outside the workspace.</summary>
    public WorkspaceProject? Find(string name) =>
        List().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal));

    /// <summary>Creates a project with a <c>main.ink</c>; returns the main file path.</summary>
    public async Task<string> CreateAsync(string name, string mainText, CancellationToken cancellationToken = default)
    {
        var folder = CreateFolder(name);
        var main = Path.Join(folder, MainFileName);
        await _fileSystem.WriteAllBytesAtomicAsync(main, TextFileCodec.Encode(mainText, TextFormat.Default with { LineEnding = LineEnding.Lf }), cancellationToken);
        return main;
    }

    /// <summary>
    /// Imports files (relative path, bytes) as a new project. The main file is <c>main.ink</c> if
    /// present, else <paramref name="preferredMain"/>, else the only top-level ink file, else the
    /// first top-level one. Returns the main file path.
    /// </summary>
    public async Task<string> ImportAsync(string name, IReadOnlyList<(string Path, byte[] Contents)> files, string? preferredMain = null, CancellationToken cancellationToken = default)
    {
        List<(string Path, byte[] Contents)> safe =
        [
            .. files
                .Select(f => (Path: ProjectArchive.SafeRelativePath(f.Path), f.Contents))
                .Where(f => f.Path is not null && ProjectPaths.IsInkFile(f.Path))
                .Select(f => (f.Path!, f.Contents)),
        ];
        var topLevel = safe.Where(f => !f.Path.Contains('/', StringComparison.Ordinal)).Select(f => f.Path).ToList();
        var main = topLevel.FirstOrDefault(p => p.Equals(MainFileName, StringComparison.OrdinalIgnoreCase))
            ?? topLevel.FirstOrDefault(p => p == preferredMain)
            ?? topLevel.Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            ?? throw new InvalidDataException("There are no top-level .ink files to import.");

        safe = PlaceIncludedFiles(safe, main);

        var folder = CreateFolder(name);
        foreach (var (path, contents) in safe)
        {
            await _fileSystem.WriteAllBytesAtomicAsync(Path.Join(folder, path), contents, cancellationToken);
        }

        return Path.Join(folder, main);
    }

    /// <summary>
    /// Moves files to the paths the INCLUDE lines (followed from the main file) expect. Picking
    /// files in a browser yields bare names, so <c>INCLUDE chapters/forest.ink</c> would otherwise
    /// miss the imported <c>forest.ink</c>. A missing include takes a not-yet-included file with the
    /// same name, preferring top-level ones. Include paths are relative to the project root, as in ink.
    /// </summary>
    private static List<(string Path, byte[] Contents)> PlaceIncludedFiles(List<(string Path, byte[] Contents)> files, string main)
    {
        var placed = files.ToList();
        HashSet<string> included = [main];
        Queue<string> pending = new([main]);
        while (pending.TryDequeue(out var current))
        {
            var (text, _) = TextFileCodec.Decode(placed.First(f => f.Path == current).Contents);
            foreach (var include in IncludeScanner.Scan(text))
            {
                if (ProjectArchive.SafeRelativePath(include.Path) is not { } target || included.Contains(target))
                {
                    continue;
                }

                var index = placed.FindIndex(f => f.Path == target);
                if (index < 0)
                {
                    var fileName = Path.GetFileName(target);
                    var candidates = placed
                        .Select((f, i) => (f.Path, Index: i))
                        .Where(f => !included.Contains(f.Path) && string.Equals(Path.GetFileName(f.Path), fileName, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => f.Path.Count(c => c == '/'))
                        .ToList();
                    if (candidates.Count == 0)
                    {
                        continue;
                    }

                    index = candidates[0].Index;
                    placed[index] = (target, placed[index].Contents);
                }

                included.Add(target);
                pending.Enqueue(target);
            }
        }

        return placed;
    }

    /// <summary>Included files (relative paths) that don't exist, following INCLUDE lines from the main file.</summary>
    public static IReadOnlyList<string> MissingIncludes(string mainFilePath)
    {
        var project = InkProject.Open(mainFilePath);
        return [.. project.Files.Where(f => !f.ExistsOnDisk).Select(f => f.RelativePath.Replace('\\', '/'))];
    }

    public void Delete(WorkspaceProject project)
    {
        var folder = Path.GetDirectoryName(project.MainFilePath)!;
        if (IsInside(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The name of the workspace project containing <paramref name="path"/>, or null if it's outside the workspace.</summary>
    public string? ProjectNameOf(string path)
    {
        if (!IsInside(path))
        {
            return null;
        }

        var relative = Path.GetRelativePath(RootDirectory, Path.GetFullPath(path));
        return relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])[0];
    }

    /// <summary>True if the path is inside the workspace.</summary>
    public bool IsInside(string path)
    {
        var relative = Path.GetRelativePath(RootDirectory, Path.GetFullPath(path));
        return relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathFullyQualified(relative);
    }

    /// <summary>A project name that isn't taken, based on <paramref name="name"/> (e.g. <c>story 2</c>).</summary>
    public string UniqueName(string name)
    {
        var baseName = SanitizeName(name);
        var candidate = baseName;
        for (var i = 2; Directory.Exists(Path.Join(RootDirectory, candidate)); i++)
        {
            candidate = $"{baseName} {i}";
        }

        return candidate;
    }

    /// <summary>Makes a user-entered name safe as a folder name.</summary>
    public static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var cleaned = new string([.. name.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c)]).Trim('.', ' ');
        return cleaned.Length == 0 ? "story" : cleaned;
    }

    private string CreateFolder(string name)
    {
        var folder = Path.Join(RootDirectory, UniqueName(name));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string? FindMainFile(string folder)
    {
        var main = Path.Join(folder, MainFileName);
        if (_fileSystem.FileExists(main))
        {
            return main;
        }

        var topLevel = _fileSystem.EnumerateFiles(folder, "*.ink", recursive: false).Order(StringComparer.OrdinalIgnoreCase).ToList();
        return topLevel.Count > 0 ? topLevel[0] : null;
    }
}
