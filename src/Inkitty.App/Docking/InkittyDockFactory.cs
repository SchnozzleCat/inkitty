using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using Dock.Settings;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Docking;

/// <summary>
/// Builds Inkitty's docking layout. The default is three panes, like Inky:
/// Files (with the Outline below) | Editor (with Issues and Variables tabbed below) | Player (with the Story Map below).
/// Every tool can be moved, tabbed, floated or pinned.
/// </summary>
public sealed class InkittyDockFactory(FileTreeToolViewModel files, PlayerToolViewModel player, IssuesToolViewModel issues, StoryMapToolViewModel storyMap, VariablesToolViewModel variables, OutlineToolViewModel outline) : Factory
{
    private IRootDock? _root;
    private IDocumentDock? _documents;

    public FileTreeToolViewModel FilesTool { get; } = files;

    public PlayerToolViewModel PlayerTool { get; } = player;

    public IssuesToolViewModel IssuesTool { get; } = issues;

    public StoryMapToolViewModel StoryMapTool { get; } = storyMap;

    public VariablesToolViewModel VariablesTool { get; } = variables;

    public OutlineToolViewModel OutlineTool { get; } = outline;

    public IDocumentDock? DocumentDock => _documents;

    public IRootDock? Root => _root;

    public IEnumerable<ITool> Tools => [FilesTool, PlayerTool, IssuesTool, StoryMapTool, VariablesTool, OutlineTool];

    public override IRootDock CreateLayout()
    {
        var filesDock = new ToolDock
        {
            Id = DockIds.LeftDock,
            Proportion = 0.35,
            Alignment = Alignment.Left,
            ActiveDockable = FilesTool,
            VisibleDockables = CreateList<IDockable>(FilesTool),
        };

        // The outline of the active file sits under the file list.
        var outlineDock = new ToolDock
        {
            Id = DockIds.OutlineDock,
            Proportion = 0.65,
            Alignment = Alignment.Left,
            ActiveDockable = OutlineTool,
            VisibleDockables = CreateList<IDockable>(OutlineTool),
        };

        var leftColumn = new ProportionalDock
        {
            Id = DockIds.LeftColumn,
            Proportion = 0.18,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(filesDock, new ProportionalDockSplitter(), outlineDock),
        };

        var documents = new DocumentDock
        {
            Id = DockIds.Documents,
            Title = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            EnableWindowDrag = true,
            VisibleDockables = CreateList<IDockable>(),
        };

        var issuesDock = new ToolDock
        {
            Id = DockIds.BottomDock,
            Proportion = 0.25,
            Alignment = Alignment.Bottom,
            ActiveDockable = IssuesTool,
            VisibleDockables = CreateList<IDockable>(IssuesTool, VariablesTool),
        };

        var center = new ProportionalDock
        {
            Id = DockIds.CenterDock,
            Proportion = 0.52,
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(documents, new ProportionalDockSplitter(), issuesDock),
        };

        var playerDock = new ToolDock
        {
            Id = DockIds.RightDock,
            Proportion = 0.58,
            Alignment = Alignment.Right,
            ActiveDockable = PlayerTool,
            VisibleDockables = CreateList<IDockable>(PlayerTool),
        };

        // The map sits under the player, so both are visible while playing.
        var mapDock = new ToolDock
        {
            Id = DockIds.MapDock,
            Proportion = 0.42,
            Alignment = Alignment.Right,
            ActiveDockable = StoryMapTool,
            VisibleDockables = CreateList<IDockable>(StoryMapTool),
        };

        var rightColumn = new ProportionalDock
        {
            Id = DockIds.RightColumn,
            Proportion = 0.30,
            Orientation = Orientation.Vertical,
            VisibleDockables = CreateList<IDockable>(playerDock, new ProportionalDockSplitter(), mapDock),
        };

        var main = new ProportionalDock
        {
            Id = DockIds.MainLayout,
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = CreateList<IDockable>(
                leftColumn,
                new ProportionalDockSplitter(),
                center,
                new ProportionalDockSplitter(),
                rightColumn),
        };

        var root = CreateRootDock();
        root.Id = DockIds.Root;
        root.IsCollapsable = false;
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        root.VisibleDockables = CreateList<IDockable>(main);
        root.HiddenDockables = CreateList<IDockable>();
        root.LeftPinnedDockables = CreateList<IDockable>();
        root.RightPinnedDockables = CreateList<IDockable>();
        root.TopPinnedDockables = CreateList<IDockable>();
        root.BottomPinnedDockables = CreateList<IDockable>();
        return root;
    }

    public override void InitLayout(IDockable layout)
    {
        // Tools are singletons: closing one hides it, and View > ... brings it back.
        HideToolsOnClose = true;
        HideDocumentsOnClose = false;

        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [DockIds.Root] = () => _root,
            [DockIds.Documents] = () => _documents,
            [DockIds.Files] = () => FilesTool,
            [DockIds.Player] = () => PlayerTool,
            [DockIds.Issues] = () => IssuesTool,
            [DockIds.StoryMap] = () => StoryMapTool,
            [DockIds.Variables] = () => VariablesTool,
            [DockIds.Outline] = () => OutlineTool,
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            // Managed hosts live inside the main window (see Inkitty.Desktop.Program.UseManagedDockWindows; always in the browser).
            [nameof(IDockWindow)] = () => DockSettings.IsManagedWindowHostingEnabled()
                ? new ManagedHostWindow()
                : new HostWindow(),
        };

        base.InitLayout(layout);

        _root = layout as IRootDock;
        _documents = FindDocumentDock(layout);
    }

    // In-window (managed) floating panels are part of the main window's visual tree, so while one is being
    // moved, Dock's drop search hits the panel itself whenever the pointer is over its body - e.g. once the
    // panel is stopped by the window's bottom edge and the pointer slides off its header towards the bottom
    // dock selector. Docking a panel onto itself is invalid, so every selector disappeared. Hide the moving
    // panel from hit testing for the duration of the drag (its pointer capture still delivers the moves).
    private readonly List<Avalonia.Controls.Control> _hiddenFromHitTest = [];

    public override bool OnWindowMoveDragBegin(IDockWindow? window)
    {
        if (!base.OnWindowMoveDragBegin(window))
        {
            return false;
        }

        RestoreHitTesting();
        if (window?.Host is ManagedHostWindow)
        {
            foreach (var control in DockControls.OfType<Avalonia.Visual>()
                .SelectMany(Avalonia.VisualTree.VisualExtensions.GetVisualDescendants)
                .OfType<MdiDocumentWindow>()
                .Where(w => w.DataContext is ManagedDockWindowDocument document && ReferenceEquals(document.Window, window)))
            {
                control.IsHitTestVisible = false;
                _hiddenFromHitTest.Add(control);
            }
        }

        return true;
    }

    public override void OnWindowMoveDragEnd(IDockWindow? window)
    {
        RestoreHitTesting();
        base.OnWindowMoveDragEnd(window);
    }

    private void RestoreHitTesting()
    {
        foreach (var control in _hiddenFromHitTest)
        {
            control.IsHitTestVisible = true;
        }

        _hiddenFromHitTest.Clear();
    }

    /// <summary>
    /// A floating pane is its own root dock, and Dock hides a closed tool in the root it's in: the tool
    /// stayed in the floating window (left open and empty, and reopened there by View), or was lost with
    /// it once that window closed. Closed tools always go to the main window's hidden list instead,
    /// remembering their old dock there if it still exists, and an emptied floating window goes away.
    /// </summary>
    protected override void CloseDockableCore(IDockable dockable)
    {
        if (dockable is not ITool tool || _root is null || FindRoot(tool, _ => true) is not { } root || ReferenceEquals(root, _root))
        {
            base.CloseDockableCore(dockable);
            return;
        }

        if (!OnDockableClosing(tool))
        {
            return;
        }

        var home = tool.Owner is IDock owner ? FindMainDock(owner.Id) : null;
        RemoveDockable(tool, collapse: true);
        HideInMainWindow(tool, home);
        if (root.Window is { } window && !HasTools(root) && _root.Windows?.Contains(window) == true)
        {
            RemoveWindow(window);
        }

        OnDockableClosed(tool);
    }

    private void HideInMainWindow(ITool tool, IDock? home)
    {
        _root!.HiddenDockables ??= CreateList<IDockable>();
        tool.Owner = _root;
        tool.OriginalOwner = home;
        _root.HiddenDockables.Add(tool);
        OnDockableHidden(tool);
    }

    /// <summary>A tool dock in the main window with this id (floating panes reuse their source dock's id).</summary>
    private IDock? FindMainDock(string? id) =>
        _root is null || string.IsNullOrEmpty(id) ? null : FindDockable(_root, d => d is IToolDock && d.Id == id) as IDock;

    private static bool HasTools(IDockable dockable) => dockable switch
    {
        ITool => true,
        IDock dock => dock.VisibleDockables?.Any(HasTools) == true,
        _ => false,
    };

    /// <summary>Raised whenever panes move, are added, removed, pinned, unpinned or float (headers refresh).</summary>
    public event EventHandler? LayoutChanged;

    private void RaiseLayoutChanged() => LayoutChanged?.Invoke(this, EventArgs.Empty);

    public override void OnDockableAdded(IDockable? dockable) { base.OnDockableAdded(dockable); RaiseLayoutChanged(); }

    public override void OnDockableRemoved(IDockable? dockable) { base.OnDockableRemoved(dockable); RaiseLayoutChanged(); }

    public override void OnDockableMoved(IDockable? dockable) { base.OnDockableMoved(dockable); RaiseLayoutChanged(); }

    public override void OnDockableDocked(IDockable? dockable, DockOperation operation) { base.OnDockableDocked(dockable, operation); RaiseLayoutChanged(); }

    public override void OnDockableUndocked(IDockable? dockable, DockOperation operation) { base.OnDockableUndocked(dockable, operation); RaiseLayoutChanged(); }

    public override void OnDockablePinned(IDockable? dockable) { base.OnDockablePinned(dockable); RaiseLayoutChanged(); }

    public override void OnDockableUnpinned(IDockable? dockable) { base.OnDockableUnpinned(dockable); RaiseLayoutChanged(); }

    public override void OnDockableHidden(IDockable? dockable) { base.OnDockableHidden(dockable); RaiseLayoutChanged(); }

    public override void OnDockableRestored(IDockable? dockable) { base.OnDockableRestored(dockable); RaiseLayoutChanged(); }

    public override void OnWindowAdded(IDockWindow? window) { base.OnWindowAdded(window); RaiseLayoutChanged(); }

    public override void OnWindowRemoved(IDockWindow? window) { base.OnWindowRemoved(window); RaiseLayoutChanged(); }

    /// <summary>
    /// True if the tool can be auto-hidden (pinned to the window edge) where it is now: anywhere in the main
    /// window, but in a floating window only while another tool stays visible there, since auto-hiding the
    /// only one would leave an empty window. Unpinning is always possible.
    /// </summary>
    public bool CanPinTool(IDockable? tool)
    {
        if (tool is not ITool { CanPin: true } || FindRoot(tool, _ => true) is not { } root)
        {
            return false;
        }

        if (IsDockablePinned(tool, root))
        {
            return true;
        }

        return tool.Owner is IToolDock && (ReferenceEquals(root, _root) || CountVisibleTools(root) > 1);
    }

    private static int CountVisibleTools(IDockable dockable) => dockable switch
    {
        ITool => 1,
        IDock dock => dock.VisibleDockables?.Sum(CountVisibleTools) ?? 0,
        _ => 0,
    };

    public override void PinDockable(IDockable dockable)
    {
        if (dockable is ITool && !CanPinTool(dockable))
        {
            return;
        }

        base.PinDockable(dockable);
    }

    /// <summary>
    /// True if the tool's header is its floating window's title bar: the window holds just its tool dock
    /// (Dock then draws a tool-style title bar). Not when the window is split into several panes, nor while
    /// the tool slides out from the edge after being auto-hidden.
    /// </summary>
    public bool IsInSinglePaneFloatingWindow(IDockable tool) =>
        FindRoot(tool, _ => true) is { } root && !ReferenceEquals(root, _root)
        && root.ActiveDockable is IToolDock paneDock && ReferenceEquals(tool.Owner, paneDock);

    /// <summary>True if the dockable is in the main window's layout (not a floating window).</summary>
    public bool IsInMainWindow(IDockable dockable) => _root is not null && ReferenceEquals(FindRoot(dockable, _ => true), _root);

    /// <summary>The floating window a tool is shown in, or null if it isn't in one that's open.</summary>
    private IRootDock? FloatingRootOf(ITool tool) =>
        FindRoot(tool, _ => true) is { Window: { } window } root && !ReferenceEquals(root, _root) && _root?.Windows?.Contains(window) == true
            ? root
            : null;

    /// <summary>Makes a hidden or closed tool visible again, restoring it to its last dock.</summary>
    public void ShowTool(ITool tool)
    {
        if (_root is null)
        {
            return;
        }

        // Left behind outside the main window (hidden in a floating pane, or in one that's since closed,
        // e.g. from a layout saved before closing worked as above): bring it back from there.
        if (FindRoot(tool, _ => true) is { } root && !ReferenceEquals(root, _root)
            && (root.HiddenDockables?.Contains(tool) == true || FloatingRootOf(tool) is null))
        {
            var home = tool.Owner is IDock lastOwner and not IRootDock ? FindMainDock(lastOwner.Id) : null;
            if (root.HiddenDockables?.Remove(tool) != true && tool.Owner is IDock stale)
            {
                stale.VisibleDockables?.Remove(tool);
            }

            HideInMainWindow(tool, home);
        }

        if (_root.HiddenDockables?.Contains(tool) == true)
        {
            RestoreDockable(tool);

            // Its old dock has since left the layout (e.g. collapsed when emptied): place it afresh below.
            if (!ReferenceEquals(FindRoot(tool, _ => true), _root))
            {
                (tool.Owner as IDock)?.VisibleDockables?.Remove(tool);
                tool.Owner = null;
            }
        }

        if (tool.Owner is null)
        {
            // Not anywhere in the layout (e.g. after a broken restore): put it next to the documents.
            if (_documents is not null)
            {
                var dock = new ToolDock { Alignment = Alignment.Bottom, Proportion = 0.25, VisibleDockables = CreateList<IDockable>() };
                AddDockable(dock, tool);
                dock.ActiveDockable = tool;
                SplitToDock(_documents, dock, DockOperation.Bottom);
            }
        }

        if (IsDockablePinned(tool))
        {
            // Auto-hidden: slide it out.
            PreviewPinnedDockable(tool);
            return;
        }

        if (tool.Owner is IDock owner)
        {
            SetActiveDockable(tool);
            SetFocusedDockable(owner, tool);
        }
    }

    /// <summary>True if the tool is shown, in the main window or an open floating one.</summary>
    public bool IsToolVisible(ITool tool) =>
        tool.Owner is IDock and not IRootDock
        && (ReferenceEquals(FindRoot(tool, _ => true), _root) || FloatingRootOf(tool) is not null);

    private static IDocumentDock? FindDocumentDock(IDockable dockable) => dockable switch
    {
        IDocumentDock documentDock => documentDock,
        IDock dock => dock.VisibleDockables?.Select(FindDocumentDock).FirstOrDefault(d => d is not null),
        _ => null,
    };
}
