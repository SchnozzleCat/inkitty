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
/// Files | Editor (with Issues below) | Player (with the Story Map below).
/// Every tool can be moved, tabbed, floated or pinned.
/// </summary>
public sealed class InkittyDockFactory(FileTreeToolViewModel files, PlayerToolViewModel player, IssuesToolViewModel issues, StoryMapToolViewModel storyMap) : Factory
{
    private IRootDock? _root;
    private IDocumentDock? _documents;

    public FileTreeToolViewModel FilesTool { get; } = files;

    public PlayerToolViewModel PlayerTool { get; } = player;

    public IssuesToolViewModel IssuesTool { get; } = issues;

    public StoryMapToolViewModel StoryMapTool { get; } = storyMap;

    public IDocumentDock? DocumentDock => _documents;

    public IRootDock? Root => _root;

    public IEnumerable<ITool> Tools => [FilesTool, PlayerTool, IssuesTool, StoryMapTool];

    public override IRootDock CreateLayout()
    {
        var filesDock = new ToolDock
        {
            Id = DockIds.LeftDock,
            Proportion = 0.18,
            Alignment = Alignment.Left,
            ActiveDockable = FilesTool,
            VisibleDockables = CreateList<IDockable>(FilesTool),
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
            VisibleDockables = CreateList<IDockable>(IssuesTool),
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
                filesDock,
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
        };

        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            // Managed hosts live inside the main window (see Program.UseManagedDockWindows).
            [nameof(IDockWindow)] = () => DockSettings.IsManagedWindowHostingEnabled()
                ? new ManagedHostWindow()
                : new HostWindow(),
        };

        base.InitLayout(layout);

        _root = layout as IRootDock;
        _documents = FindDocumentDock(layout);
    }

    /// <summary>Makes a hidden or closed tool visible again, restoring it to its last dock.</summary>
    public void ShowTool(ITool tool)
    {
        if (_root is null)
        {
            return;
        }

        if (_root.HiddenDockables?.Contains(tool) == true)
        {
            RestoreDockable(tool);
        }

        if (tool.Owner is null)
        {
            // Not anywhere in the layout (e.g. after a broken restore): put it next to the documents.
            if (_documents?.Owner is IDock center)
            {
                var dock = new ToolDock { Alignment = Alignment.Bottom, Proportion = 0.25, VisibleDockables = CreateList<IDockable>() };
                AddDockable(center, dock);
                AddDockable(dock, tool);
            }
        }

        if (tool.Owner is IDock owner)
        {
            SetActiveDockable(tool);
            SetFocusedDockable(owner, tool);
        }
    }

    public bool IsToolVisible(ITool tool) =>
        tool.Owner is not null && _root?.HiddenDockables?.Contains(tool) != true;

    private static IDocumentDock? FindDocumentDock(IDockable dockable) => dockable switch
    {
        IDocumentDock documentDock => documentDock,
        IDock dock => dock.VisibleDockables?.Select(FindDocumentDock).FirstOrDefault(d => d is not null),
        _ => null,
    };
}
