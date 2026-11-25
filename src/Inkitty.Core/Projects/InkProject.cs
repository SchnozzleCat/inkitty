using Inkitty.Core.Diagnostics;
using Inkitty.Core.FileSystem;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.Projects;

/// <summary>Outcome of re-scanning the include graph.</summary>
/// <param name="StructureChanged">Files were added to or removed from the project.</param>
/// <param name="CreatedFiles">Files that were referenced by an INCLUDE but didn't exist; created as new, unsaved buffers.</param>
/// <param name="Issues">Problems with INCLUDE lines themselves (e.g. paths escaping the project root).</param>
public sealed record IncludeRefreshResult(bool StructureChanged, IReadOnlyList<InkSourceFile> CreatedFiles, IReadOnlyList<Issue> Issues);

/// <summary>
/// A project is a main ink file plus everything it (transitively) includes. There is no project
/// file: like Inky, the structure is inferred from <c>INCLUDE</c> lines, which resolve relative to
/// the folder containing the main file.
/// </summary>
public sealed class InkProject
{
    private readonly IFileSystem _fileSystem;
    private readonly Dictionary<string, InkSourceFile> _loaded = new(PathComparer.Default);
    private List<InkSourceFile> _files = [];
    private List<Issue> _includeIssues = [];

    private InkProject(string rootDirectory, string mainFile, IFileSystem fileSystem)
    {
        RootDirectory = rootDirectory;
        MainFile = mainFile;
        _fileSystem = fileSystem;
    }

    public string RootDirectory { get; }

    /// <summary>Relative path of the main ink file.</summary>
    public string MainFile { get; }

    public string Name => Path.GetFileNameWithoutExtension(MainFile);

    /// <summary>Files in the project, in include (depth-first) order, starting with the main file.</summary>
    public IReadOnlyList<InkSourceFile> Files => _files;

    /// <summary>Issues from the last include scan.</summary>
    public IReadOnlyList<Issue> IncludeIssues => _includeIssues;

    public InkSourceFile Main => _loaded[MainFile];

    /// <summary>Raised when files are added to or removed from <see cref="Files"/>.</summary>
    public event EventHandler? StructureChanged;

    public static InkProject Open(string mainFilePath, IFileSystem? fileSystem = null)
    {
        fileSystem ??= PhysicalFileSystem.Instance;
        var full = Path.GetFullPath(mainFilePath);
        if (!fileSystem.FileExists(full))
        {
            throw new FileNotFoundException($"Main ink file not found: {full}", full);
        }

        var root = Path.GetDirectoryName(full)!;
        var project = new InkProject(root, Path.GetFileName(full), fileSystem);
        project.RefreshIncludes();
        return project;
    }

    /// <summary>Creates a new project with a template main file on disk.</summary>
    public static async Task<InkProject> CreateAsync(string mainFilePath, string template, IFileSystem? fileSystem = null)
    {
        fileSystem ??= PhysicalFileSystem.Instance;
        var full = Path.GetFullPath(mainFilePath);
        await fileSystem.WriteAllBytesAtomicAsync(full, TextFileCodec.Encode(template, TextFormat.Default));
        return Open(full, fileSystem);
    }

    public InkSourceFile? Find(string relativeOrFullPath)
    {
        var relative = ProjectPaths.Normalize(RootDirectory, relativeOrFullPath);
        return relative is not null && _loaded.TryGetValue(relative, out var file) && _files.Contains(file) ? file : null;
    }

    /// <summary>Index of a file in include order (for sorting issues), or int.MaxValue.</summary>
    public int IncludeOrderOf(string relativePath)
    {
        for (var i = 0; i < _files.Count; i++)
        {
            if (PathComparer.PathEquals(_files[i].RelativePath, relativePath))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>
    /// Re-scans INCLUDE lines starting at the main file. Loads newly referenced files from disk,
    /// and creates empty in-memory buffers for referenced files that don't exist yet (Inky behaviour);
    /// they are written on the next save. Cheap enough to call after every edit.
    /// </summary>
    public IncludeRefreshResult RefreshIncludes()
    {
        var mainFile = GetOrLoad(MainFile, createIfMissing: false)
            ?? throw new FileNotFoundException("Main ink file is missing.", Path.Join(RootDirectory, MainFile));

        List<InkSourceFile> ordered = [];
        List<InkSourceFile> created = [];
        List<Issue> issues = [];
        HashSet<string> visited = [with(PathComparer.Default)];

        void Visit(InkSourceFile file)
        {
            if (!visited.Add(file.RelativePath))
            {
                return;
            }

            ordered.Add(file);
            foreach (var include in IncludeScanner.Scan(file.Text))
            {
                var relative = ProjectPaths.Normalize(RootDirectory, include.Path);
                if (relative is null)
                {
                    issues.Add(new Issue(
                        IssueSeverity.Error,
                        $"INCLUDE '{include.Path}' must refer to a file inside the project folder.",
                        SourceRange.WholeLine(file.RelativePath, include.Line),
                        IssueOrigin.Project));
                    continue;
                }

                // Only auto-create files that look like ink files, so half-typed INCLUDE lines
                // ("INCLUDE chap") don't spawn stray buffers.
                var existed = _loaded.ContainsKey(relative) || _fileSystem.FileExists(ProjectPaths.ToFullPath(RootDirectory, relative));
                if (GetOrLoad(relative, createIfMissing: ProjectPaths.IsInkFile(relative)) is { } child)
                {
                    if (!existed)
                    {
                        created.Add(child);
                    }

                    Visit(child);
                }
            }
        }

        Visit(mainFile);

        // Forget buffers that dropped out of the project, unless they hold edits worth keeping.
        foreach (var stale in _loaded.Values.Where(f => !visited.Contains(f.RelativePath)).ToList())
        {
            if (!stale.ExistsOnDisk && string.IsNullOrEmpty(stale.Text) || !stale.IsDirty)
            {
                _loaded.Remove(stale.RelativePath);
            }
        }

        var structureChanged = !ordered.SequenceEqual(_files);
        _files = ordered;
        _includeIssues = issues;
        if (structureChanged)
        {
            StructureChanged?.Invoke(this, EventArgs.Empty);
        }

        return new IncludeRefreshResult(structureChanged, created, issues);
    }

    /// <summary>
    /// <c>.ink</c> files under the project root that aren't part of the include graph,
    /// as relative paths (shown greyed out in the file tree).
    /// </summary>
    public IReadOnlyList<string> FindUnincludedFiles()
    {
        HashSet<string> included = [with(PathComparer.Default), .. _files.Select(f => f.RelativePath)];
        return
        [
            .. _fileSystem.EnumerateFiles(RootDirectory, "*.ink", recursive: true)
                .Select(p => ProjectPaths.Normalize(RootDirectory, p))
                .OfType<string>()
                .Where(p => !included.Contains(p) && !p.Split('/').Any(segment => segment.StartsWith('.')))
                .Order(StringComparer.OrdinalIgnoreCase),
        ];
    }

    public ProjectSnapshot CreateSnapshot() =>
        new(RootDirectory, MainFile, _files.Select(f => (f.RelativePath, f.Text, f.Version)));

    /// <summary>Saves one file atomically, preserving its line endings and BOM.</summary>
    public async Task SaveAsync(InkSourceFile file, CancellationToken cancellationToken = default)
    {
        var text = file.Text;
        var bytes = TextFileCodec.Encode(text, file.Format);
        await _fileSystem.WriteAllBytesAtomicAsync(file.FullPath, bytes, cancellationToken);
        file.MarkSaved(text, bytes);
    }

    /// <summary>Saves every dirty file. Files in conflict are skipped unless <paramref name="includeConflicted"/>.</summary>
    public async Task<IReadOnlyList<InkSourceFile>> SaveAllAsync(bool includeConflicted = true, CancellationToken cancellationToken = default)
    {
        List<InkSourceFile> saved = [];
        foreach (var file in _files.Where(f => f.IsDirty && (includeConflicted || !f.IsInConflict)))
        {
            await SaveAsync(file, cancellationToken);
            saved.Add(file);
        }

        return saved;
    }

    /// <summary>
    /// Reads a file's current bytes from disk. Returns null if the file doesn't exist.
    /// Used by the file watcher to decide whether a change is external.
    /// </summary>
    public async Task<byte[]?> ReadFromDiskAsync(InkSourceFile file, CancellationToken cancellationToken = default) =>
        _fileSystem.FileExists(file.FullPath) ? await _fileSystem.ReadAllBytesAsync(file.FullPath, cancellationToken) : null;

    /// <summary>Gets a loaded file by relative path, including files outside the include graph.</summary>
    internal InkSourceFile? GetLoaded(string relativePath) => _loaded.GetValueOrDefault(relativePath);

    private InkSourceFile? GetOrLoad(string relativePath, bool createIfMissing)
    {
        if (_loaded.TryGetValue(relativePath, out var existing))
        {
            return existing;
        }

        var fullPath = ProjectPaths.ToFullPath(RootDirectory, relativePath);
        InkSourceFile file;
        if (_fileSystem.FileExists(fullPath))
        {
            var bytes = _fileSystem.ReadAllBytes(fullPath);
            var (text, format) = TextFileCodec.Decode(bytes);
            file = new InkSourceFile(relativePath, fullPath, text, format, TextFileCodec.Hash(bytes));
        }
        else if (createIfMissing)
        {
            file = new InkSourceFile(relativePath, fullPath, string.Empty, Main.Format, diskHash: null);
            file.MarkDeletedOnDisk(); // new buffer: dirty and not on disk, written on next save
        }
        else
        {
            return null;
        }

        _loaded[relativePath] = file;
        return file;
    }
}
