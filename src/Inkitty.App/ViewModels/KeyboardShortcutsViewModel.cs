using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inkitty.App.Input;

namespace Inkitty.App.ViewModels;

/// <summary>
/// Backs the keyboard shortcuts editor: a filterable list of commands, each with its shortcut, which the
/// user changes by recording a new key. Changes apply and save immediately.
/// </summary>
public sealed partial class KeyboardShortcutsViewModel : ViewModelBase, IDisposable
{
    private readonly ShortcutService _shortcuts;
    private IDisposable? _suspension;

    public KeyboardShortcutsViewModel(ShortcutService shortcuts)
    {
        _shortcuts = shortcuts;
        All = ShortcutCatalog.Commands.Select(c => new ShortcutRowViewModel(c, this)).ToList();
        Rows = All;
        _shortcuts.Changed += OnShortcutsChanged;
        Refresh();
    }

    public IReadOnlyList<ShortcutRowViewModel> All { get; }

    /// <summary>The rows matching <see cref="Filter"/>.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ShortcutRowViewModel> Rows { get; private set; }

    /// <summary>Matches command names, categories and shortcuts.</summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>The row waiting for a key, if any.</summary>
    [ObservableProperty]
    public partial ShortcutRowViewModel? Recording { get; private set; }

    /// <summary>Why the last key pressed while recording wasn't accepted.</summary>
    [ObservableProperty]
    public partial string? RecordingHint { get; private set; }

    public bool IsBrowser => _shortcuts.Platform.IsBrowser;

    internal ShortcutService Shortcuts => _shortcuts;

    partial void OnFilterChanged(string value)
    {
        var terms = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Rows = terms.Length == 0 ? All : All.Where(r => terms.All(r.Matches)).ToList();
    }

    public void StartRecording(ShortcutRowViewModel row)
    {
        if (Recording == row)
        {
            return;
        }

        if (Recording is { } previous)
        {
            previous.IsRecording = false;
        }

        Recording = row;
        row.IsRecording = true;
        RecordingHint = null;
        _suspension ??= _shortcuts.Suspend();
    }

    public void StopRecording()
    {
        if (Recording is { } row)
        {
            row.IsRecording = false;
        }

        Recording = null;
        RecordingHint = null;
        _suspension?.Dispose();
        _suspension = null;
    }

    /// <summary>
    /// A key pressed while recording. Returns true if it was used (assigned, or Esc to cancel); a lone
    /// modifier is ignored so the user can go on to press the rest of the shortcut.
    /// </summary>
    public bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (Recording is not { } row)
        {
            return false;
        }

        if (ShortcutCatalog.IsModifierKey(key))
        {
            return false;
        }

        if (key == Key.Escape && modifiers == KeyModifiers.None)
        {
            StopRecording();
            return true;
        }

        var gesture = new KeyGesture(key, modifiers);
        if (!ShortcutCatalog.IsAllowed(gesture))
        {
            RecordingHint = $"{ShortcutCatalog.Format(gesture, _shortcuts.Platform)} would stop that key being typed. "
                + $"Use {(_shortcuts.Platform.UsesMacSymbols ? "Cmd, Ctrl or Option" : "Ctrl or Alt")} with it, or an F key.";
            return true;
        }

        StopRecording();
        _shortcuts.Set(row.Id, gesture);
        return true;
    }

    [RelayCommand]
    private void ResetAll()
    {
        StopRecording();
        _shortcuts.ResetAll();
    }

    private void OnShortcutsChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        foreach (var row in All)
        {
            row.Refresh();
        }

        OnPropertyChanged(nameof(HasCustomisations));
    }

    public bool HasCustomisations => All.Any(r => r.IsCustomised);

    public void Dispose()
    {
        StopRecording();
        _shortcuts.Changed -= OnShortcutsChanged;
    }
}

/// <summary>One command in the shortcuts editor.</summary>
public sealed partial class ShortcutRowViewModel : ViewModelBase
{
    private readonly ShortcutCommand _command;
    private readonly KeyboardShortcutsViewModel _owner;

    public ShortcutRowViewModel(ShortcutCommand command, KeyboardShortcutsViewModel owner)
    {
        _command = command;
        _owner = owner;
    }

    public string Id => _command.Id;

    public string Category => _command.Category;

    public string Name => _command.Name;

    [ObservableProperty]
    public partial string GestureText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCustomised { get; private set; }

    /// <summary>A warning: the shortcut is shared with another command, or the browser keeps it.</summary>
    [ObservableProperty]
    public partial string? Warning { get; private set; }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    public bool HasGesture => GestureText.Length > 0;

    /// <summary>What the shortcut button shows.</summary>
    public string ButtonText => IsRecording ? "Press a shortcut…" : HasGesture ? GestureText : "None";

    public string DefaultText => "Default: " + (ShortcutCatalog.Format(_owner.Shortcuts.DefaultFor(Id), _owner.Shortcuts.Platform) is { Length: > 0 } text ? text : "none");

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(ButtonText));

    partial void OnGestureTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasGesture));
        OnPropertyChanged(nameof(ButtonText));
    }

    internal bool Matches(string term) =>
        Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || Category.Contains(term, StringComparison.OrdinalIgnoreCase)
        || GestureText.Contains(term, StringComparison.OrdinalIgnoreCase);

    internal void Refresh()
    {
        var shortcuts = _owner.Shortcuts;
        var gesture = shortcuts.GestureFor(Id);
        GestureText = ShortcutCatalog.Format(gesture, shortcuts.Platform);
        IsCustomised = shortcuts.IsCustomised(Id);
        var conflicts = shortcuts.ConflictsWith(Id);
        Warning = conflicts.Count > 0
            ? "Also the shortcut for " + string.Join(", ", conflicts.Select(c => c.Name))
            : gesture is not null && shortcuts.Platform.IsBrowser && ShortcutCatalog.IsBrowserReserved(gesture, shortcuts.Platform)
                ? "Your browser keeps this shortcut for itself"
                : null;
    }

    [RelayCommand]
    private void Record()
    {
        if (IsRecording)
        {
            _owner.StopRecording();
        }
        else
        {
            _owner.StartRecording(this);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _owner.StopRecording();
        _owner.Shortcuts.Set(Id, null);
    }

    [RelayCommand]
    private void Reset()
    {
        _owner.StopRecording();
        _owner.Shortcuts.Reset(Id);
    }
}
