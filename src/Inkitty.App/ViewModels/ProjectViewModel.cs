using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Inkitty.App.Messages;
using Inkitty.App.Services;
using Inkitty.App.ViewModels.Documents;
using Inkitty.Core.Compilation;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.FileSystem;
using Inkitty.Core.Projects;
using Inkitty.Core.Settings;
using Inkitty.Core.StoryMap;
using Inkitty.Core.Symbols;
using Microsoft.Extensions.Logging;

namespace Inkitty.App.ViewModels;

/// <summary>
/// An open project: the files, live compilation, file watching and the latest compile result.
/// All public members are UI-thread only.
/// </summary>
public sealed partial class ProjectViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IMessenger _messenger;
    private readonly ILogger _logger;
    private readonly LiveCompileScheduler _scheduler;
    private readonly ProjectWatcher? _watcher;
    private readonly Dictionary<InkSourceFile, EditorDocumentViewModel> _documents = [];
    private IReadOnlyList<Issue> _compileIssues = [];
    private IReadOnlyList<Issue> _runtimeIssues = [];

    public ProjectViewModel(
        InkProject project,
        IInkCompilerService compiler,
        ISettingsStore settings,
        IUiDispatcher dispatcher,
        IMessenger messenger,
        ILoggerFactory loggerFactory,
        TimeProvider? time = null,
        bool watchFiles = true)
    {
        Project = project;
        Name = project.Name;
        _dispatcher = dispatcher;
        _messenger = messenger;
        _logger = loggerFactory.CreateLogger<ProjectViewModel>();

        _scheduler = new LiveCompileScheduler(
            compiler,
            _ => _dispatcher.InvokeAsync(TakeSnapshot),
            TimeSpan.FromMilliseconds(settings.Current.CompileDebounceMs),
            time);
        _scheduler.Compiled += (_, result) => _dispatcher.Post(() => ApplyCompileResult(result));
        _scheduler.BusyChanged += (_, busy) => _dispatcher.Post(() => IsCompiling = busy);
        _scheduler.Faulted += (_, ex) => _logger.LogError(ex, "Compilation failed unexpectedly");
        settings.Changed += (_, s) => _scheduler.Debounce = TimeSpan.FromMilliseconds(s.CompileDebounceMs);

        if (watchFiles)
        {
            _watcher = new ProjectWatcher(project.RootDirectory, time, logger: loggerFactory.CreateLogger<ProjectWatcher>());
            _watcher.FilesChanged += (_, paths) => _dispatcher.Post(() => _ = HandleExternalChangesAsync(paths));
            _watcher.Overflowed += (_, _) => _dispatcher.Post(() => _ = RescanAllAsync());
        }

        RefreshFileList();
        Project.StructureChanged += (_, _) => RefreshFileList();
    }

    public InkProject Project { get; }

    /// <summary>Shown in the title, the Files pane and export names. Defaults to the main file's name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Files in include order.</summary>
    public ObservableCollection<InkSourceFile> Files { get; } = [];

    /// <summary>.ink files in the folder that aren't included anywhere.</summary>
    public ObservableCollection<string> UnincludedFiles { get; } = [];

    [ObservableProperty]
    public partial CompileResult? LastResult { get; private set; }

    /// <summary>The latest successful compile (the player keeps running it while there are errors).</summary>
    [ObservableProperty]
    public partial CompileSucceeded? LastSuccess { get; private set; }

    [ObservableProperty]
    public partial bool IsCompiling { get; private set; }

    /// <summary>Compile, project and runtime issues together.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Issue> Issues { get; private set; } = [];

    /// <summary>Where the player is in the story; decorates the editors.</summary>
    [ObservableProperty]
    public partial StoryProgress Progress { get; private set; } = StoryProgress.Empty;

    /// <summary>Symbol index matching the current buffer versions, or null if it's stale.</summary>
    public SymbolIndex? CurrentSymbols { get; private set; }

    public IEnumerable<EditorDocumentViewModel> OpenDocuments => _documents.Values;

    /// <summary>Raised after every compile result is applied (UI thread).</summary>
    public event EventHandler<CompileResult>? Compiled;

    /// <summary>Raised when a document view model is created for a file.</summary>
    public event EventHandler<EditorDocumentViewModel>? DocumentCreated;

    /// <summary>Gets (creating if needed) the document for a file.</summary>
    public EditorDocumentViewModel GetDocument(InkSourceFile file)
    {
        if (!_documents.TryGetValue(file, out var document))
        {
            document = new EditorDocumentViewModel(file, this);
            document.Issues = IssuesFor(file.RelativePath);
            document.Playthrough = Progress.ForFile(file.RelativePath);
            _documents[file] = document;
            DocumentCreated?.Invoke(this, document);
        }

        return document;
    }

    public EditorDocumentViewModel? FindDocument(string relativePath) =>
        Project.Find(relativePath) is { } file ? GetDocument(file) : null;

    public void RequestCompile() => _scheduler.Request();

    public void RequestCompileNow() => _scheduler.RequestNow();

    /// <summary>Called by documents on every edit.</summary>
    internal void OnBufferEdited(InkSourceFile file)
    {
        CurrentSymbols = null; // ranges are stale until the next compile
        _scheduler.Request();
    }

    public void SetRuntimeIssues(IReadOnlyList<Issue> issues)
    {
        _runtimeIssues = issues;
        UpdateIssues();
    }

    public void SetProgress(StoryProgress progress)
    {
        Progress = progress;
        foreach (var (file, document) in _documents)
        {
            document.Playthrough = progress.ForFile(file.RelativePath);
        }
    }

    public async Task SaveAsync(InkSourceFile file)
    {
        await Project.SaveAsync(file);
        RefreshUnincluded();
        _scheduler.RequestNow();
    }

    public async Task<int> SaveAllAsync(bool includeConflicted = true)
    {
        var saved = await Project.SaveAllAsync(includeConflicted);
        if (saved.Count > 0)
        {
            RefreshUnincluded();
            _scheduler.RequestNow();
        }

        return saved.Count;
    }

    /// <summary>Looks up the reference at a position, using the fallback scanner if the index is stale.</summary>
    public SymbolReference? FindReferenceAt(InkSourceFile file, SourcePosition position)
    {
        if (CurrentSymbols is { } symbols && LastResult?.Snapshot.VersionOf(file.RelativePath) == file.Version)
        {
            return symbols.FindReferenceAt(file.RelativePath, position);
        }

        var definitions = FallbackSymbolScanner.ScanDefinitions(Project.Files.Select(f => (f.RelativePath, f.Text)));
        return FallbackSymbolScanner.ResolveAt(file.RelativePath, file.Text, position, definitions);
    }

    public bool GoToDefinition(InkSourceFile file, SourcePosition position)
    {
        if (FindReferenceAt(file, position)?.Target is not { } target)
        {
            return false;
        }

        _messenger.Send(new NavigateToSourceMessage(target.Range.File, target.Range));
        return true;
    }

    /// <summary>Re-checks every file against disk (on window activation, or after watcher overflow).</summary>
    public Task RescanAllAsync() =>
        HandleExternalChangesAsync([.. Project.Files.Select(f => f.FullPath)]);

    public async ValueTask DisposeAsync()
    {
        await _scheduler.DisposeAsync();
        if (_watcher is not null)
        {
            await _watcher.DisposeAsync();
        }

        foreach (var document in _documents.Values)
        {
            document.Dispose();
        }
    }

    private ProjectSnapshot TakeSnapshot()
    {
        var refresh = Project.RefreshIncludes();
        foreach (var created in refresh.CreatedFiles)
        {
            _messenger.Send(new StatusMessage($"Created {created.RelativePath} (unsaved)"));
        }

        return Project.CreateSnapshot();
    }

    private void ApplyCompileResult(CompileResult result)
    {
        if (result.Generation != _scheduler.LatestGeneration)
        {
            return;
        }

        LastResult = result;
        if (result is CompileSucceeded success)
        {
            LastSuccess = success;
        }

        // The index is current only if no buffer changed since the snapshot was taken.
        CurrentSymbols = Project.Files.All(f => result.Snapshot.VersionOf(f.RelativePath) == f.Version) ? result.Symbols : null;
        _compileIssues = result.Issues;
        UpdateIssues();
        Compiled?.Invoke(this, result);
    }

    private void UpdateIssues()
    {
        List<Issue> all = [.. Project.IncludeIssues, .. _compileIssues, .. _runtimeIssues];
        Issues =
        [
            .. all.Distinct()
                .OrderBy(i => i.Severity.Rank)
                .ThenBy(i => i.File is null ? -1 : Project.IncludeOrderOf(i.File))
                .ThenBy(i => i.Line ?? 0),
        ];

        foreach (var (file, document) in _documents)
        {
            document.Issues = IssuesFor(file.RelativePath);
        }
    }

    private IReadOnlyList<Issue> IssuesFor(string relativePath) =>
        [.. Issues.Where(i => i.File is { } f && PathComparer.PathEquals(f, relativePath))];

    private void RefreshFileList()
    {
        SyncCollection(Files, Project.Files);
        RefreshUnincluded();
    }

    private void RefreshUnincluded() => SyncCollection(UnincludedFiles, Project.FindUnincludedFiles());

    private static void SyncCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        if (target.SequenceEqual(source))
        {
            return;
        }

        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private async Task HandleExternalChangesAsync(IReadOnlyCollection<string> fullPaths)
    {
        var anyChange = false;
        foreach (var path in fullPaths)
        {
            var file = Project.Files.FirstOrDefault(f => PathComparer.PathEquals(f.FullPath, path));
            if (file is null)
            {
                anyChange = true; // a file was created/removed: may affect includes or the "not included" list
                continue;
            }

            byte[]? bytes;
            try
            {
                bytes = await Project.ReadFromDiskAsync(file);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not read {File}", file.FullPath);
                continue;
            }

            if (bytes is null)
            {
                if (file.ExistsOnDisk)
                {
                    file.MarkDeletedOnDisk();
                    _documents.GetValueOrDefault(file)?.ShowDeletedOnDisk();
                    anyChange = true;
                }

                continue;
            }

            if (TextFileCodec.Hash(bytes) == file.DiskHash)
            {
                continue; // our own save, or a no-op touch
            }

            if (file.IsDirty)
            {
                GetDocument(file).ShowChangedOnDisk(bytes);
            }
            else
            {
                file.LoadFromDisk(bytes);
                _messenger.Send(new StatusMessage($"Reloaded {file.RelativePath} (changed on disk)"));
                anyChange = true;
            }
        }

        if (anyChange)
        {
            RefreshUnincluded();
            _scheduler.Request();
        }
    }
}
