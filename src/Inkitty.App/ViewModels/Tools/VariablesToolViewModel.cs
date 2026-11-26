using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Messages;
using Inkitty.Core.Playback;
using Inkitty.Core.Symbols;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>One row in the variables pane.</summary>
public sealed partial class VariableItem(StoryVariable variable, bool justChanged) : ObservableObject
{
    public StoryVariable Variable { get; } = variable;

    public string Name => Variable.Name;

    public string Value => Variable.Value;

    /// <summary>Different from the value it was declared with.</summary>
    public bool IsChanged => Variable.IsChanged;

    /// <summary>Changed by the most recent step of the story (highlighted).</summary>
    public bool JustChanged { get; } = justChanged;

    public string KindClass => Variable.Kind switch
    {
        StoryVariableKind.Boolean => "bool",
        StoryVariableKind.WholeNumber or StoryVariableKind.FractionalNumber => "number",
        StoryVariableKind.Text => "string",
        StoryVariableKind.List => "list",
        StoryVariableKind.DivertTarget => "divert",
        _ => "other",
    };

    /// <summary>Tooltip: only when it adds something (the starting value of a changed variable).</summary>
    public string? Detail => IsChanged ? $"Started as {Variable.DefaultValue}" : null;
}

/// <summary>
/// The variables pane: every global variable (VAR, LIST) at the player's current position, with the ones
/// the last step changed highlighted. Mirrors the player, so it always matches the transcript.
/// </summary>
public sealed partial class VariablesToolViewModel : Tool
{
    private readonly PlayerToolViewModel _player;
    private readonly IMessenger _messenger;
    private Dictionary<string, string> _previous = [];
    private HashSet<string> _justChanged = [];
    private ProjectViewModel? _project;

    public VariablesToolViewModel(PlayerToolViewModel player, IMessenger messenger)
    {
        _player = player;
        _messenger = messenger;
        Id = DockIds.Variables;
        Title = "Variables";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    public ObservableCollection<VariableItem> Items { get; } = [];

    [ObservableProperty]
    public partial VariableItem? SelectedItem { get; set; }

    /// <summary>Filters by name or value (case-insensitive).</summary>
    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    /// <summary>Show only variables that differ from their declared value.</summary>
    [ObservableProperty]
    public partial bool ChangedOnly { get; set; }

    [ObservableProperty]
    public partial string Placeholder { get; private set; } = "Open an ink file to see its variables.";

    public bool HasItems => Items.Count > 0;

    /// <summary>Called by the shell when a project opens or closes (for go-to-declaration).</summary>
    public void Attach(ProjectViewModel? project)
    {
        _project = project;
        _previous = [];
        _justChanged = [];
        Refresh();
    }

    /// <summary>Jumps to where the variable is declared.</summary>
    [RelayCommand]
    private void Open(VariableItem? item)
    {
        item ??= SelectedItem;
        var symbols = _project?.LastResult?.Symbols;
        if (item is null || symbols is null)
        {
            return;
        }

        var declaration = symbols.FindByName(item.Name)
            .FirstOrDefault(d => d.Kind is SymbolKind.Variable or SymbolKind.List);
        if (declaration is not null)
        {
            _messenger.Send(new NavigateToSourceMessage(declaration.Range.File, declaration.Range));
        }
    }

    partial void OnFilterChanged(string value) => Refresh(sameStep: true);

    partial void OnChangedOnlyChanged(bool value) => Refresh(sameStep: true);

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerToolViewModel.Variables) or nameof(PlayerToolViewModel.HasProject))
        {
            Refresh();
        }
    }

    /// <param name="sameStep">True when only the filter changed: keep the "just changed" marks as they were.</param>
    private void Refresh(bool sameStep = false)
    {
        var variables = _player.Variables;
        if (!sameStep)
        {
            // "Just changed" compares with the previous step; a fresh project has nothing to compare to.
            var current = variables.ToDictionary(v => v.Name, v => v.Value);
            _justChanged = _previous.Count == 0
                ? []
                : [.. current.Where(kv => _previous.TryGetValue(kv.Key, out var before) && before != kv.Value).Select(kv => kv.Key)];
            _previous = current;
        }

        var selected = SelectedItem?.Name;
        Items.Clear();
        foreach (var variable in variables)
        {
            if (ChangedOnly && !variable.IsChanged)
            {
                continue;
            }

            if (Filter.Length > 0
                && !variable.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                && !variable.Value.Contains(Filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Items.Add(new VariableItem(variable, _justChanged.Contains(variable.Name)));
        }

        SelectedItem = Items.FirstOrDefault(i => i.Name == selected);
        Placeholder = !_player.HasProject ? "Open an ink file to see its variables."
            : variables.Count == 0 ? "This story has no global variables (VAR or LIST)."
            : "No variables match.";
        OnPropertyChanged(nameof(HasItems));
    }
}
