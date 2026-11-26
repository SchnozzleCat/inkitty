using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Messages;
using Inkitty.App.ViewModels.Documents;
using Inkitty.Core.Outline;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>A row in the outline tree.</summary>
public sealed partial class OutlineNode(OutlineItem item) : ObservableObject
{
    public OutlineItem Item { get; } = item;

    public string Name => Item.Name;

    public string? Detail => Item.Detail;

    public bool HasDetail => Item.Detail is not null;

    /// <summary>A choice's conditions, shown before its text.</summary>
    public string? Condition => Item.Condition;

    public bool HasCondition => Item.Condition is not null;

    /// <summary>A choice's label, e.g. <c>(shouted)</c>, shown first as in the source.</summary>
    public string? Label => Item.Label;

    public bool HasLabel => Item.Label is not null;

    public ObservableCollection<OutlineNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public string Icon => Item.Kind switch
    {
        OutlineKind.Knot => "==",
        OutlineKind.Stitch => "=",
        OutlineKind.Function => "ƒ",
        OutlineKind.Choice => "*",
        OutlineKind.StickyChoice => "+",
        OutlineKind.Gather => "-",
        OutlineKind.Variable => "v",
        OutlineKind.Constant => "c",
        OutlineKind.List => "≡",
        OutlineKind.External => "↗",
        OutlineKind.Include => "→",
        _ => string.Empty,
    };

    /// <summary>Style class for the icon colour.</summary>
    public string KindClass => Item.Kind switch
    {
        OutlineKind.Knot or OutlineKind.Stitch or OutlineKind.Function => "flow",
        OutlineKind.Choice or OutlineKind.StickyChoice or OutlineKind.Gather => "weave",
        _ => "declaration",
    };

    public bool IsFlow => Item.IsFlow;
}

/// <summary>
/// The outline of the active file: declarations, knots, stitches, functions, and choices and labelled
/// gathers nested as they are in the weave. Updates as you type; follows the caret.
/// </summary>
public sealed partial class OutlineToolViewModel : Tool
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(250);

    private readonly IMessenger _messenger;
    private readonly Services.IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    /// <summary>Expanded/collapsed state the user chose, by item key (unknown items use the default).</summary>
    private readonly Dictionary<string, bool> _expansion = [];
    private EditorDocumentViewModel? _document;
    private ITimer? _refreshTimer;
    private bool _navigating;

    public OutlineToolViewModel(IMessenger messenger, Services.IUiDispatcher dispatcher, TimeProvider? time = null)
    {
        _messenger = messenger;
        _dispatcher = dispatcher;
        _time = time ?? TimeProvider.System;
        Id = DockIds.Outline;
        Title = "Outline";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
    }

    public ObservableCollection<OutlineNode> Nodes { get; } = [];

    /// <summary>The element the caret is in (kept selected as the caret moves).</summary>
    [ObservableProperty]
    public partial OutlineNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string FileName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Placeholder { get; private set; } = Placeholders.NoFile;

    /// <summary>Show choices and gathers, not just knots, stitches and declarations.</summary>
    [ObservableProperty]
    public partial bool ShowWeave { get; set; } = true;

    public bool HasNodes => Nodes.Count > 0;

    /// <summary>Called by the shell when the active document changes.</summary>
    public void Show(EditorDocumentViewModel? document)
    {
        if (_document == document)
        {
            return;
        }

        if (_document is not null)
        {
            _document.Document.TextChanged -= OnTextChanged;
            _document.PropertyChanged -= OnDocumentPropertyChanged;
        }

        _document = document;
        _expansion.Clear();
        if (document is not null)
        {
            document.Document.TextChanged += OnTextChanged;
            document.PropertyChanged += OnDocumentPropertyChanged;
        }

        Rebuild();
    }

    /// <summary>Jumps the editor to an element.</summary>
    [RelayCommand]
    private void Open(OutlineNode? node)
    {
        node ??= SelectedNode;
        if (node is null || _document is null)
        {
            return;
        }

        _navigating = true;
        try
        {
            _messenger.Send(new NavigateToSourceMessage(_document.RelativePath, node.Item.Range));
        }
        finally
        {
            _navigating = false;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in All(Nodes))
        {
            node.IsExpanded = false;
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var node in All(Nodes))
        {
            node.IsExpanded = true;
        }
    }

    partial void OnShowWeaveChanged(bool value) => Rebuild();

    private void OnTextChanged(object? sender, EventArgs e)
    {
        // Debounced: outline after a pause in typing.
        _refreshTimer?.Dispose();
        _refreshTimer = _time.CreateTimer(_ => _dispatcher.Post(Rebuild), null, RefreshDelay, Timeout.InfiniteTimeSpan);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorDocumentViewModel.Caret) && !_navigating)
        {
            SelectAtCaret();
        }
    }

    private void Rebuild()
    {
        foreach (var node in All(Nodes).Where(n => n.Children.Count > 0))
        {
            _expansion[node.Item.Key] = node.IsExpanded;
        }

        Nodes.Clear();
        FileName = _document?.Title ?? string.Empty;
        if (_document is not { } document)
        {
            Placeholder = Placeholders.NoFile;
            OnPropertyChanged(nameof(HasNodes));
            return;
        }

        var items = OutlineScanner.Scan(document.RelativePath, document.Document.Text);
        foreach (var item in items)
        {
            if (ToNode(item) is { } node)
            {
                Nodes.Add(node);
            }
        }

        Placeholder = "Nothing to outline yet. Knots (=== name ===), stitches, choices and VARs appear here.";
        OnPropertyChanged(nameof(HasNodes));
        SelectAtCaret();
    }

    private OutlineNode? ToNode(OutlineItem item)
    {
        if (!ShowWeave && item.IsWeave)
        {
            return null;
        }

        var node = new OutlineNode(item);
        foreach (var child in item.Children)
        {
            if (ToNode(child) is { } childNode)
            {
                node.Children.Add(childNode);
            }
        }

        // Knots and stitches start open; choices start closed to keep long weaves compact.
        node.IsExpanded = node.Children.Count > 0 && _expansion.GetValueOrDefault(item.Key, item.IsFlow);
        return node;
    }

    private void SelectAtCaret()
    {
        if (_document is null)
        {
            return;
        }

        var line = _document.Caret.Line;
        OutlineNode? best = null;
        List<OutlineNode> path = [];
        void Walk(IEnumerable<OutlineNode> nodes, List<OutlineNode> ancestors)
        {
            foreach (var node in nodes)
            {
                if (node.Item.Line > line)
                {
                    return;
                }

                best = node;
                path = [.. ancestors];
                Walk(node.Children, [.. ancestors, node]);
            }
        }

        Walk(Nodes, []);
        foreach (var ancestor in path)
        {
            ancestor.IsExpanded = true; // reveal the caret's element
        }

        SelectedNode = best;
    }

    private static IEnumerable<OutlineNode> All(IEnumerable<OutlineNode> nodes) =>
        nodes.SelectMany(n => (IEnumerable<OutlineNode>)[n, .. All(n.Children)]);
}
