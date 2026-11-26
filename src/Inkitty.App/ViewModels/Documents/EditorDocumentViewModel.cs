using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;
using Inkitty.Core.StoryMap;
using Inkitty.Core.Symbols;

namespace Inkitty.App.ViewModels.Documents;

public enum InfoBarKind
{
    ChangedOnDisk,
    DeletedOnDisk,
}

/// <summary>A non-modal notice shown above the editor (e.g. the file changed on disk).</summary>
public sealed record DocumentInfoBar(InfoBarKind Kind, string Message);

/// <summary>
/// One ink file open in the editor. Owns the AvaloniaEdit <see cref="TextDocument"/> (so undo history
/// survives closing and reopening the tab) and keeps it in sync with the project's <see cref="InkSourceFile"/>.
/// </summary>
public sealed partial class EditorDocumentViewModel : ViewModelBase, IDisposable
{
    private readonly ProjectViewModel _project;
    private bool _syncing;
    private byte[]? _pendingDiskBytes;

    public EditorDocumentViewModel(InkSourceFile file, ProjectViewModel project)
    {
        File = file;
        _project = project;
        Document = new TextDocument(file.Text) { FileName = file.FullPath };
        Document.UndoStack.ClearAll();
        Document.TextChanged += OnDocumentTextChanged;
        File.Changed += OnFileChanged;
    }

    public InkSourceFile File { get; }

    public TextDocument Document { get; }

    public string RelativePath => File.RelativePath;

    public string Title => File.FileName;

    public bool IsDirty => File.IsDirty;

    /// <summary>Tab title including a dirty marker.</summary>
    public string TabTitle => File.IsDirty ? $"{File.FileName} ●" : File.FileName;

    /// <summary>Issues in this file from the latest compile (and the player).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Issue> Issues { get; set; } = [];

    /// <summary>Lines the current playthrough has been through, is at, or is offering choices on.</summary>
    [ObservableProperty]
    public partial FileProgress Playthrough { get; set; } = FileProgress.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInfoBar), nameof(InfoBarMessage), nameof(IsChangedOnDisk), nameof(IsDeletedOnDisk))]
    public partial DocumentInfoBar? InfoBar { get; set; }

    public bool HasInfoBar => InfoBar is not null;

    public string InfoBarMessage => InfoBar?.Message ?? string.Empty;

    public bool IsChangedOnDisk => InfoBar?.Kind == InfoBarKind.ChangedOnDisk;

    public bool IsDeletedOnDisk => InfoBar?.Kind == InfoBarKind.DeletedOnDisk;

    /// <summary>Caret position (1-based), reported by the editor control.</summary>
    [ObservableProperty]
    public partial SourcePosition Caret { get; set; } = new(1, 1);

    /// <summary>Navigation the editor control hasn't applied yet (e.g. before the tab's view exists).</summary>
    public SourceRange? PendingNavigation { get; private set; }

    /// <summary>Raised when the caret should move to a range; the editor control handles it.</summary>
    public event EventHandler<SourceRange>? NavigationRequested;

    public void RequestNavigate(SourceRange range)
    {
        PendingNavigation = range;
        NavigationRequested?.Invoke(this, range);
    }

    /// <summary>Called by the editor control once it has applied <see cref="PendingNavigation"/>.</summary>
    public void ClearPendingNavigation() => PendingNavigation = null;

    /// <summary>The symbol reference at a position, for hover underlines.</summary>
    public SymbolReference? FindReferenceAt(SourcePosition position) => _project.FindReferenceAt(File, position);

    /// <summary>Jumps to the definition of the symbol at a position. Returns false if there is none.</summary>
    public bool GoToDefinitionAt(SourcePosition position) => _project.GoToDefinition(File, position);

    [RelayCommand]
    private void ReloadFromDisk()
    {
        if (_pendingDiskBytes is { } bytes)
        {
            File.LoadFromDisk(bytes);
        }

        _pendingDiskBytes = null;
        InfoBar = null;
        _project.RequestCompile();
    }

    [RelayCommand]
    private void KeepMine()
    {
        // Stays dirty and in conflict until the next explicit save overwrites the disk version.
        _pendingDiskBytes = null;
        InfoBar = null;
    }

    [RelayCommand]
    private void DismissInfoBar() => InfoBar = null;

    internal void ShowChangedOnDisk(byte[] diskBytes)
    {
        _pendingDiskBytes = diskBytes;
        File.IsInConflict = true;
        InfoBar = new DocumentInfoBar(InfoBarKind.ChangedOnDisk, $"{File.FileName} was changed on disk while you have unsaved edits.");
    }

    internal void ShowDeletedOnDisk() =>
        InfoBar = new DocumentInfoBar(InfoBarKind.DeletedOnDisk, $"{File.FileName} was deleted on disk. Saving will recreate it.");

    public void Dispose()
    {
        Document.TextChanged -= OnDocumentTextChanged;
        File.Changed -= OnFileChanged;
    }

    private void OnDocumentTextChanged(object? sender, EventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            File.SetText(Document.Text);
        }
        finally
        {
            _syncing = false;
        }

        _project.OnBufferEdited(File);
    }

    private void OnFileChanged(object? sender, EventArgs e)
    {
        if (!_syncing && !string.Equals(File.Text, Document.Text, StringComparison.Ordinal))
        {
            // Reloaded from disk: replace the text as one undoable step.
            _syncing = true;
            try
            {
                Document.Replace(0, Document.TextLength, File.Text);
            }
            finally
            {
                _syncing = false;
            }
        }

        if (File.ExistsOnDisk && InfoBar?.Kind == InfoBarKind.DeletedOnDisk)
        {
            InfoBar = null;
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(TabTitle));
    }
}
