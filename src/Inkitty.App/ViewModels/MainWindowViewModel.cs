using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Controls;
using Dock.Model.Core;
using Inkitty.App.Docking;
using Inkitty.App.Messages;
using Inkitty.App.Services;
using Inkitty.App.ViewModels.Documents;
using Inkitty.Core.Compilation;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;
using Inkitty.Core.Settings;
using Inkitty.Core.Symbols;
using Microsoft.Extensions.Logging;

namespace Inkitty.App.ViewModels;

/// <summary>The application shell: menu commands, the dock layout, the open project and the status bar.</summary>
public sealed partial class MainWindowViewModel : ViewModelBase, IRecipient<NavigateToSourceMessage>, IRecipient<StatusMessage>, IAsyncDisposable
{
    private const string NewProjectTemplate = """
        // Welcome to Inkitty! Start writing your story here.
        Once upon a time...

        * [There were two choices.] They lived happily ever after.
            -> END
        * [There were four lines of content.]
            -> END
        """;

    private readonly IInkCompilerService _compiler;
    private readonly ISettingsStore _settings;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly AutoSaveService _autoSave;
    private readonly DockLayoutService _layoutService;
    private readonly TimeProvider _time;
    private readonly bool _watchFiles;
    private ITimer? _statusTimer;

    public MainWindowViewModel(
        InkittyDockFactory factory,
        IInkCompilerService compiler,
        ISettingsStore settings,
        IUiDispatcher dispatcher,
        IDialogService dialogs,
        IMessenger messenger,
        AutoSaveService autoSave,
        DockLayoutService layoutService,
        ILoggerFactory loggerFactory,
        TimeProvider? time = null,
        bool watchFiles = true,
        ThemeService? themes = null)
    {
        Factory = factory;
        _compiler = compiler;
        _settings = settings;
        _dispatcher = dispatcher;
        _dialogs = dialogs;
        _messenger = messenger;
        _autoSave = autoSave;
        _layoutService = layoutService;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<MainWindowViewModel>();
        _time = time ?? TimeProvider.System;
        _watchFiles = watchFiles;

        _messenger.RegisterAll(this);
        _settings.Changed += (_, _) => _dispatcher.Post(() => OnPropertyChanged(nameof(Settings)));
        if (themes is not null)
        {
            SyntaxTheme = themes.Current?.TextMateTheme;
            themes.ThemeChanged += (_, theme) => SyntaxTheme = theme.TextMateTheme;
        }
        _autoSave.Saved += (_, count) => ShowStatus(count == 1 ? "Auto-saved" : $"Auto-saved {count} files");
        History.Changed += (_, _) =>
        {
            GoBackCommand.NotifyCanExecuteChanged();
            GoForwardCommand.NotifyCanExecuteChanged();
        };

        Layout = _layoutService.TryLoad(factory) ?? factory.CreateLayout();
        factory.InitLayout(Layout);
        Factory.ActiveDockableChanged += OnActiveDockableChanged;
        Factory.DockableClosed += OnDockableClosed;
        Factory.PlayerTool.RevealRequested += (_, location) =>
            Receive(new NavigateToSourceMessage(location.File!, SourceRange.WholeLine(location.File!, location.Line)));
        UpdateTitle();
    }

    public InkittyDockFactory Factory { get; }

    public NavigationHistory History { get; } = new();

    [ObservableProperty]
    public partial IRootDock Layout { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAllCommand), nameof(RestartStoryCommand), nameof(RevealStoryPositionCommand), nameof(ExportProjectCommand))]
    public partial ProjectViewModel? Project { get; private set; }

    [ObservableProperty]
    public partial EditorDocumentViewModel? ActiveDocument { get; private set; }

    partial void OnActiveDocumentChanged(EditorDocumentViewModel? value) => Factory.OutlineTool.Show(value);

    /// <summary>Names opened projects (the browser uses the workspace folder name); null uses the main file's name.</summary>
    public Func<InkProject, string?>? ProjectNamer { get; init; }

    [ObservableProperty]
    public partial string WindowTitle { get; private set; } = "Inkitty";

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "Open an ink file to start (Ctrl+O)";

    [ObservableProperty]
    public partial string CompileStatus { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCompiling { get; private set; }

    [ObservableProperty]
    public partial int ErrorCount { get; private set; }

    [ObservableProperty]
    public partial int WarningCount { get; private set; }

    [ObservableProperty]
    public partial int TodoCount { get; private set; }

    [ObservableProperty]
    public partial string CaretText { get; private set; } = string.Empty;

    public IReadOnlyList<string> RecentProjects => _settings.Current.RecentProjects;

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public AppSettings Settings => _settings.Current;

    /// <summary>The editor's TextMate theme, from the selected colour theme.</summary>
    [ObservableProperty]
    public partial TextMateSharp.Grammars.ThemeName? SyntaxTheme { get; private set; }

    /// <summary>Raised when the view should show the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the view should quit the application.</summary>
    public event EventHandler? ExitRequested;

    public async Task OpenProjectAsync(string mainFilePath)
    {
        InkProject project;
        try
        {
            project = InkProject.Open(mainFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Couldn't open project", ex.Message);
            return;
        }

        if (!await CloseProjectAsync())
        {
            return;
        }

        var vm = new ProjectViewModel(project, _compiler, _settings, _dispatcher, _messenger, _loggerFactory, _time, _watchFiles)
        {
            Name = ProjectNamer?.Invoke(project) ?? project.Name,
        };
        vm.Compiled += OnCompiled;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProjectViewModel.IsCompiling))
            {
                IsCompiling = vm.IsCompiling;
            }
            else if (e.PropertyName == nameof(ProjectViewModel.Issues))
            {
                UpdateIssueCounts(vm.Issues);
            }
        };
        vm.DocumentCreated += (_, doc) =>
        {
            doc.PropertyChanged += OnDocumentPropertyChanged;
            doc.Document.TextChanged += (_, _) => _autoSave.OnEdited(doc.File);
        };

        Project = vm;
        Factory.FilesTool.Attach(vm);
        Factory.PlayerTool.Attach(vm);
        Factory.VariablesTool.Attach(vm);
        Factory.IssuesTool.HasProject = true;
        Factory.IssuesTool.SetIssues([]);
        _autoSave.Attach(vm);
        _settings.Update(s => s.WithRecentProject(project.Main.FullPath));
        OnPropertyChanged(nameof(RecentProjects));
        OnPropertyChanged(nameof(HasRecentProjects));

        OpenDocument(project.Main);
        UpdateTitle();
        ShowStatus($"Opened {vm.Name}");
        vm.RequestCompileNow();
    }

    /// <summary>Closes the current project, asking about unsaved changes. Returns false if cancelled.</summary>
    public async Task<bool> CloseProjectAsync()
    {
        if (Project is not { } project)
        {
            return true;
        }

        if (!await ConfirmUnsavedAsync(project.Project.Files.Where(f => f.IsDirty).ToList(), $"Project '{project.Name}'"))
        {
            return false;
        }

        foreach (var doc in DocumentDockables().ToList())
        {
            doc.CloseGuard = null;
            Factory.CloseDockable(doc);
        }

        Factory.FilesTool.Attach(null);
        Factory.PlayerTool.Attach(null);
        Factory.VariablesTool.Attach(null);
        Factory.IssuesTool.HasProject = false;
        Factory.IssuesTool.SetIssues([]);
        _autoSave.Attach(null);
        History.Clear();
        Project = null;
        ActiveDocument = null;
        await project.DisposeAsync();
        UpdateIssueCounts([]);
        CompileStatus = string.Empty;
        UpdateTitle();
        return true;
    }

    /// <summary>Called when the main window is closing. Returns false to cancel.</summary>
    public async Task<bool> OnClosingAsync()
    {
        if (!await CloseProjectAsync())
        {
            return false;
        }

        _layoutService.Save(Layout);
        await _settings.FlushAsync();
        return true;
    }

    /// <summary>Called when the main window is re-activated: picks up changes missed by the watcher.</summary>
    public Task OnWindowActivatedAsync() => Project?.RescanAllAsync() ?? Task.CompletedTask;

    public Task OnWindowDeactivatedAsync() => _autoSave.OnFocusLostAsync();

    public void Receive(NavigateToSourceMessage message)
    {
        if (Project?.Project.Find(message.File) is not { } file)
        {
            ShowStatus($"{message.File} is not part of the project");
            return;
        }

        if (message.RecordHistory && ActiveDocument is { } current)
        {
            History.Push(new NavigationLocation(current.RelativePath, current.Caret));
        }

        var doc = OpenDocument(file);
        if (message.Range is { } range)
        {
            doc.RequestNavigate(range);
        }
    }

    public void Receive(StatusMessage message) => ShowStatus(message.Text);

    public EditorDocumentViewModel OpenDocument(InkSourceFile file)
    {
        var project = Project ?? throw new InvalidOperationException("No project is open.");
        var editor = project.GetDocument(file);
        var dockable = DocumentDockables().FirstOrDefault(d => d.Editor == editor);
        if (dockable is null)
        {
            dockable = new EditorDocumentDockable(editor) { CloseGuard = GuardClose };
            if (Factory.DocumentDock is { } documents)
            {
                Factory.AddDockable(documents, dockable);
            }
        }

        Factory.SetActiveDockable(dockable);
        if (dockable.Owner is IDock owner)
        {
            Factory.SetFocusedDockable(owner, dockable);
        }

        ActiveDocument = editor;
        return editor;
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (await _dialogs.PickInkFileToOpenAsync() is { } path)
        {
            await OpenProjectAsync(path);
        }
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string path)
    {
        if (!File.Exists(path))
        {
            _settings.Update(s => s with { RecentProjects = [.. s.RecentProjects.Where(p => p != path)] });
            OnPropertyChanged(nameof(RecentProjects));
            OnPropertyChanged(nameof(HasRecentProjects));
            await _dialogs.ShowErrorAsync("File not found", $"{path} no longer exists.");
            return;
        }

        await OpenProjectAsync(path);
    }

    [RelayCommand]
    private async Task NewProjectAsync()
    {
        if (await _dialogs.PickNewProjectFileAsync() is not { } path)
        {
            return;
        }

        try
        {
            await InkProject.CreateAsync(path, NewProjectTemplate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Couldn't create project", ex.Message);
            return;
        }

        await OpenProjectAsync(path);
    }

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task SaveAsync()
    {
        if (Project is not { } project || ActiveDocument is not { } doc)
        {
            return;
        }

        try
        {
            await project.SaveAsync(doc.File);
            ShowStatus($"Saved {doc.RelativePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Save failed", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task SaveAllAsync()
    {
        if (Project is not { } project)
        {
            return;
        }

        try
        {
            var count = await project.SaveAllAsync();
            ShowStatus(count switch
            {
                0 => "Nothing to save",
                1 => "Saved 1 file",
                _ => $"Saved {count} files",
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Save failed", ex.Message);
        }
    }

    /// <summary>Saves a .zip of the project's ink files (unsaved edits included) outside the project.</summary>
    [RelayCommand(CanExecute = nameof(HasProject))]
    private async Task ExportProjectAsync()
    {
        if (Project is not { } project)
        {
            return;
        }

        try
        {
            var zip = Core.Workspace.ProjectArchive.Create(project.Project);
            if (await _dialogs.SaveCopyAsync($"{project.Name}.zip", zip))
            {
                ShowStatus($"Exported {project.Name}.zip");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Export failed", ex.Message);
        }
    }

    [RelayCommand]
    private void CloseDocument()
    {
        if (DocumentDockables().FirstOrDefault(d => d.Editor == ActiveDocument) is { } dockable)
        {
            Factory.CloseDockable(dockable);
        }
    }

    [RelayCommand]
    private async Task CloseProjectCommandAsync() => await CloseProjectAsync();

    [RelayCommand(CanExecute = nameof(HasProject))]
    private void RestartStory() => Factory.PlayerTool.RestartCommand.Execute(null);

    [RelayCommand]
    private void StepBackStory()
    {
        if (Factory.PlayerTool.StepBackCommand.CanExecute(null))
        {
            Factory.PlayerTool.StepBackCommand.Execute(null);
        }
    }

    [RelayCommand]
    private void NextIssue()
    {
        Factory.IssuesTool.NextCommand.Execute(null);
    }

    [RelayCommand]
    private void PreviousIssue()
    {
        Factory.IssuesTool.PreviousCommand.Execute(null);
    }

    [RelayCommand]
    private void GoToDefinition()
    {
        if (ActiveDocument is { } doc && !doc.GoToDefinitionAt(doc.Caret))
        {
            ShowStatus("No definition found");
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        var current = CurrentLocation();
        if (History.GoBack(current) is { } target)
        {
            NavigateTo(target);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void GoForward()
    {
        var current = CurrentLocation();
        if (History.GoForward(current) is { } target)
        {
            NavigateTo(target);
        }
    }

    [RelayCommand]
    private void ShowFiles() => Factory.ShowTool(Factory.FilesTool);

    [RelayCommand]
    private void ShowPlayer() => Factory.ShowTool(Factory.PlayerTool);

    [RelayCommand]
    private void ShowIssues() => Factory.ShowTool(Factory.IssuesTool);

    [RelayCommand]
    private void ShowStoryMap() => Factory.ShowTool(Factory.StoryMapTool);

    [RelayCommand]
    private void ShowVariables() => Factory.ShowTool(Factory.VariablesTool);

    [RelayCommand]
    private void ShowOutline() => Factory.ShowTool(Factory.OutlineTool);

    /// <summary>Jumps the editor to the line the story is currently at.</summary>
    [RelayCommand(CanExecute = nameof(HasProject))]
    private void RevealStoryPosition() => Factory.PlayerTool.RevealCommand.Execute(null);

    [RelayCommand]
    private void ResetLayout()
    {
        var openDocuments = DocumentDockables().Select(d => d.Editor).ToList();
        var active = ActiveDocument;
        foreach (var doc in DocumentDockables().ToList())
        {
            doc.CloseGuard = null;
        }

        Factory.ActiveDockableChanged -= OnActiveDockableChanged;
        Factory.DockableClosed -= OnDockableClosed;
        foreach (var tool in Factory.Tools)
        {
            tool.Owner = null;
            tool.OriginalOwner = null;
        }

        var layout = Factory.CreateLayout();
        Factory.InitLayout(layout);
        Layout = layout;
        _layoutService.Delete();
        Factory.ActiveDockableChanged += OnActiveDockableChanged;
        Factory.DockableClosed += OnDockableClosed;

        foreach (var editor in openDocuments)
        {
            OpenDocument(editor.File);
        }

        if (active is not null)
        {
            OpenDocument(active.File);
        }

        ShowStatus("Layout reset");
    }

    [RelayCommand]
    private void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void Exit() => ExitRequested?.Invoke(this, EventArgs.Empty);

    private bool HasProject() => Project is not null;

    private bool CanGoBack() => History.CanGoBack;

    private bool CanGoForward() => History.CanGoForward;

    private NavigationLocation CurrentLocation() =>
        ActiveDocument is { } doc ? new NavigationLocation(doc.RelativePath, doc.Caret) : new NavigationLocation(string.Empty, new SourcePosition(1, 1));

    private void NavigateTo(NavigationLocation location)
    {
        var range = new SourceRange(location.File, location.Position, location.Position);
        Receive(new NavigateToSourceMessage(location.File, range, RecordHistory: false));
    }

    private IEnumerable<EditorDocumentDockable> DocumentDockables()
    {
        static IEnumerable<IDockable> Walk(IDockable dockable) =>
            dockable is IDock dock ? [dockable, .. (dock.VisibleDockables ?? []).SelectMany(Walk)] : [dockable];

        IEnumerable<IDockable> all = Walk(Layout);
        foreach (var window in Layout.Windows ?? [])
        {
            if (window.Layout is { } windowLayout)
            {
                all = all.Concat(Walk(windowLayout));
            }
        }

        return all.OfType<EditorDocumentDockable>();
    }

    /// <summary>Dock calls this synchronously when a tab is closed. Dirty files prompt asynchronously, then close again.</summary>
    private bool GuardClose(EditorDocumentDockable dockable)
    {
        if (!dockable.Editor.IsDirty || _settings.Current.AutoSave != AutoSaveMode.Off)
        {
            if (dockable.Editor.IsDirty)
            {
                _ = Project?.SaveAsync(dockable.Editor.File);
            }

            return true;
        }

        _ = PromptAndCloseAsync(dockable);
        return false;
    }

    private async Task PromptAndCloseAsync(EditorDocumentDockable dockable)
    {
        switch (await _dialogs.AskUnsavedChangesAsync(dockable.Editor.RelativePath))
        {
            case UnsavedChangesChoice.Save:
                await Project!.SaveAsync(dockable.Editor.File);
                break;
            case UnsavedChangesChoice.Discard:
                // Keep the buffer (so INCLUDEd files still compile) but stop tracking it as an open tab.
                break;
            default:
                return;
        }

        dockable.CloseGuard = null;
        Factory.CloseDockable(dockable);
    }

    private async Task<bool> ConfirmUnsavedAsync(IReadOnlyList<InkSourceFile> dirty, string what)
    {
        if (dirty.Count == 0 || Project is null)
        {
            return true;
        }

        switch (await _dialogs.AskUnsavedChangesAsync(dirty.Count == 1 ? dirty[0].RelativePath : what))
        {
            case UnsavedChangesChoice.Save:
                try
                {
                    await Project.SaveAllAsync();
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await _dialogs.ShowErrorAsync("Save failed", ex.Message);
                    return false;
                }

            case UnsavedChangesChoice.Discard:
                return true;
            default:
                return false;
        }
    }

    private void OnActiveDockableChanged(object? sender, Dock.Model.Core.Events.ActiveDockableChangedEventArgs e)
    {
        if (e.Dockable is EditorDocumentDockable doc && doc.Editor != ActiveDocument)
        {
            ActiveDocument = doc.Editor;
            UpdateCaretText();
            _ = _autoSave.OnFocusLostAsync();
        }
    }

    private void OnDockableClosed(object? sender, Dock.Model.Core.Events.DockableClosedEventArgs e)
    {
        if (e.Dockable is EditorDocumentDockable doc && doc.Editor == ActiveDocument)
        {
            ActiveDocument = DocumentDockables().FirstOrDefault()?.Editor;
        }
    }

    private void OnDocumentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender == ActiveDocument && e.PropertyName == nameof(EditorDocumentViewModel.Caret))
        {
            UpdateCaretText();
        }
    }

    private void UpdateCaretText() =>
        CaretText = ActiveDocument is { } doc ? $"{doc.RelativePath}  Ln {doc.Caret.Line}, Col {doc.Caret.Column}" : string.Empty;

    private void OnCompiled(object? sender, CompileResult result)
    {
        Factory.IssuesTool.SetIssues(Project?.Issues ?? result.Issues);
        CompileStatus = result switch
        {
            CompileSucceeded s => $"Compiled in {s.Duration.TotalSeconds:0.00} s",
            CompileFailed f => $"Failed to compile ({f.Issues.Count(i => i.Severity.IsError)} errors)",
        };
    }

    private void UpdateIssueCounts(IReadOnlyList<Issue> issues)
    {
        ErrorCount = issues.Count(i => i.Severity.IsError);
        WarningCount = issues.Count(i => i.Severity.IsWarning);
        TodoCount = issues.Count(i => i.Severity == IssueSeverity.Todo);
        Factory.IssuesTool.SetIssues(issues);
    }

    private void UpdateTitle() =>
        WindowTitle = Project is { } p ? $"{p.Project.MainFile} — {p.Project.RootDirectory} — Inkitty" : "Inkitty";

    private void ShowStatus(string text)
    {
        StatusText = text;
        _statusTimer?.Dispose();
        _statusTimer = _time.CreateTimer(_ => _dispatcher.Post(() => StatusText = string.Empty), null, TimeSpan.FromSeconds(6), Timeout.InfiniteTimeSpan);
    }

    partial void OnProjectChanged(ProjectViewModel? value) => OnPropertyChanged(nameof(Settings));

    public async ValueTask DisposeAsync()
    {
        _messenger.UnregisterAll(this);
        _statusTimer?.Dispose();
        if (Project is { } project)
        {
            await project.DisposeAsync();
        }
    }
}
