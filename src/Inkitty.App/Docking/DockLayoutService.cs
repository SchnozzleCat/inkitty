using System.Text.Json;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Serializer.SystemTextJson;
using Microsoft.Extensions.Logging;

namespace Inkitty.App.Docking;

/// <summary>
/// Saves and restores the dock layout (not the documents; those depend on the project).
/// Any failure, or a layout from an older version, falls back to the default layout.
/// </summary>
public sealed class DockLayoutService(string configDirectory, ILogger<DockLayoutService> logger)
{
    /// <summary>Bump when the default layout or dockable ids change incompatibly.</summary>
    public const int LayoutVersion = 4;

    private const string FileName = "layout.json";
    private const string VersionFileName = "layout.version";

    private readonly DockSerializer _serializer = new(typeof(System.Collections.ObjectModel.ObservableCollection<>));

    public string LayoutPath => Path.Join(configDirectory, FileName);

    /// <summary>The last save/load failure, for diagnostics.</summary>
    public Exception? LastError { get; private set; }

    public IRootDock? TryLoad(InkittyDockFactory factory)
    {
        var versionPath = Path.Join(configDirectory, VersionFileName);
        if (!File.Exists(LayoutPath) || !File.Exists(versionPath) || File.ReadAllText(versionPath).Trim() != LayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(LayoutPath);
            var layout = _serializer.Load<IRootDock?>(stream);
            if (layout is null)
            {
                return null;
            }

            RemoveDocuments(layout);
            RemoveEmptyWindows(layout);
            if (!ReattachTools(layout, factory))
            {
                logger.LogWarning("Saved layout is missing tools; using default layout");
                return null;
            }

            LastError = null;
            return layout;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or InvalidOperationException or InvalidCastException)
        {
            LastError = ex;
            logger.LogWarning(ex, "Could not restore dock layout; using default layout");
            return null;
        }
    }

    public void Save(IRootDock layout)
    {
        try
        {
            Directory.CreateDirectory(configDirectory);

            // Tool view models need services, so they can't be deserialised. Temporarily swap each
            // live tool for a plain placeholder Tool with the same id (and documents out entirely),
            // serialise, then put everything back.
            var swaps = SwapForPlaceholders(layout);
            string json;
            try
            {
                json = _serializer.Serialize(layout);
            }
            finally
            {
                Restore(swaps);
            }

            File.WriteAllText(LayoutPath, json);
            File.WriteAllText(Path.Join(configDirectory, VersionFileName), LayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex;
            logger.LogWarning(ex, "Could not save dock layout");
        }
    }

    private sealed record Swap(IList<IDockable> List, int Index, IDockable Original, IDock? Owner, IDockable? Active, IDockable? Focused);

    private static List<Swap> SwapForPlaceholders(IRootDock root)
    {
        List<Swap> swaps = [];
        Dictionary<string, IDockable> placeholders = [];

        IDockable Placeholder(IDockable tool) =>
            placeholders.TryGetValue(tool.Id, out var existing)
                ? existing
                : placeholders[tool.Id] = new Dock.Model.Mvvm.Controls.Tool
                {
                    Id = tool.Id,
                    Title = tool.Title,
                    CanClose = tool.CanClose,
                    CanPin = tool.CanPin,
                    CanFloat = tool.CanFloat,
                    Proportion = tool.Proportion,
                    PinnedBounds = tool.PinnedBounds,
                };

        void Visit(IList<IDockable>? list, IDock? owner)
        {
            if (list is null)
            {
                return;
            }

            for (var i = list.Count - 1; i >= 0; i--)
            {
                var dockable = list[i];
                if (dockable is ITool && dockable.GetType() != typeof(Dock.Model.Mvvm.Controls.Tool))
                {
                    swaps.Add(new Swap(list, i, dockable, owner, owner?.ActiveDockable, owner?.FocusedDockable));
                    var placeholder = Placeholder(dockable);
                    list[i] = placeholder;
                    if (owner is not null && owner.ActiveDockable == dockable)
                    {
                        owner.ActiveDockable = placeholder;
                    }

                    if (owner is not null && owner.FocusedDockable == dockable)
                    {
                        owner.FocusedDockable = placeholder;
                    }
                }
                else if (dockable is IDocument and not ITool)
                {
                    swaps.Add(new Swap(list, i, dockable, owner, owner?.ActiveDockable, owner?.FocusedDockable));
                    list.RemoveAt(i);
                    if (owner is not null && (owner.ActiveDockable == dockable || owner.FocusedDockable == dockable))
                    {
                        owner.ActiveDockable = null;
                        owner.FocusedDockable = null;
                    }
                }
                else if (dockable is IDock dock)
                {
                    Visit(dock.VisibleDockables, dock);
                }
            }
        }

        // A root's ActiveDockable/FocusedDockable can name a tool that isn't in its visible list (e.g. one
        // hidden in a floating window): swap those too, or the live tool gets serialised and can't load.
        void VisitRoot(IRootDock rootDock)
        {
            Visit(rootDock.VisibleDockables, rootDock);
            Visit(rootDock.HiddenDockables, null);
            Visit(rootDock.LeftPinnedDockables, null);
            Visit(rootDock.RightPinnedDockables, null);
            Visit(rootDock.TopPinnedDockables, null);
            Visit(rootDock.BottomPinnedDockables, null);
            if (rootDock.ActiveDockable is ITool or IDocument || rootDock.FocusedDockable is ITool or IDocument)
            {
                swaps.Add(new Swap(Array.Empty<IDockable>(), -1, rootDock, rootDock, rootDock.ActiveDockable, rootDock.FocusedDockable));
                rootDock.ActiveDockable = rootDock.ActiveDockable is ITool active ? Placeholder(active) : rootDock.ActiveDockable is IDocument ? null : rootDock.ActiveDockable;
                rootDock.FocusedDockable = rootDock.FocusedDockable is ITool focused ? Placeholder(focused) : rootDock.FocusedDockable is IDocument ? null : rootDock.FocusedDockable;
            }
        }

        VisitRoot(root);
        foreach (var window in root.Windows ?? [])
        {
            if (window.Layout is IRootDock windowRoot)
            {
                VisitRoot(windowRoot);
            }
            else if (window.Layout is { } windowLayout)
            {
                Visit(windowLayout.VisibleDockables, windowLayout);
            }
        }

        return swaps;
    }

    private static void Restore(List<Swap> swaps)
    {
        // Undo in reverse order of application so indices line up.
        for (var i = swaps.Count - 1; i >= 0; i--)
        {
            var swap = swaps[i];
            if (swap.Index < 0)
            {
                // A root's own Active/Focused swap: no list entry.
            }
            else if (swap.Original is ITool)
            {
                swap.List[swap.Index] = swap.Original;
            }
            else
            {
                swap.List.Insert(swap.Index, swap.Original);
            }

            if (swap.Owner is not null)
            {
                swap.Owner.ActiveDockable = swap.Active;
                swap.Owner.FocusedDockable = swap.Focused;
            }
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(LayoutPath);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Replaces deserialised tool instances with the app's live singletons (matched by id),
    /// so their state and services survive a layout restore.
    /// </summary>
    private static bool ReattachTools(IRootDock root, InkittyDockFactory factory)
    {
        var live = factory.Tools.ToDictionary(t => t.Id);
        var found = new HashSet<string>();

        void Visit(IList<IDockable>? list)
        {
            if (list is null)
            {
                return;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var dockable = list[i];
                if (dockable is ITool && live.TryGetValue(dockable.Id, out var tool))
                {
                    list[i] = tool;
                    found.Add(tool.Id);
                }
                else if (dockable is IDock dock)
                {
                    Visit(dock.VisibleDockables);
                    if (dock.ActiveDockable is ITool active && live.TryGetValue(active.Id, out var liveActive))
                    {
                        dock.ActiveDockable = liveActive;
                    }

                    if (dock.FocusedDockable is ITool focused && live.TryGetValue(focused.Id, out var liveFocused))
                    {
                        dock.FocusedDockable = liveFocused;
                    }
                }
            }
        }

        void VisitRoot(IRootDock rootDock)
        {
            Visit(rootDock.VisibleDockables);
            Visit(rootDock.HiddenDockables);
            Visit(rootDock.LeftPinnedDockables);
            Visit(rootDock.RightPinnedDockables);
            Visit(rootDock.TopPinnedDockables);
            Visit(rootDock.BottomPinnedDockables);
            if (rootDock.ActiveDockable is ITool active && live.TryGetValue(active.Id, out var liveActive))
            {
                rootDock.ActiveDockable = liveActive;
            }

            if (rootDock.FocusedDockable is ITool focused && live.TryGetValue(focused.Id, out var liveFocused))
            {
                rootDock.FocusedDockable = liveFocused;
            }
        }

        VisitRoot(root);
        foreach (var window in root.Windows ?? [])
        {
            if (window.Layout is IRootDock windowRoot)
            {
                VisitRoot(windowRoot);
            }
            else if (window.Layout is { } windowLayout)
            {
                Visit(windowLayout.VisibleDockables);
            }
        }

        // Tools missing entirely from the saved layout get re-added via View menu; still require the
        // document dock to exist so the editor has somewhere to live.
        return ContainsDocumentDock(root) && found.Count > 0;
    }

    /// <summary>
    /// Drops floating windows with no tools in them: closing a floating tool used to leave its window
    /// behind, empty, and layouts saved then would bring it back on every start. A tool hidden in such
    /// a window moves to the main window's hidden list, so View can still show it.
    /// </summary>
    private static void RemoveEmptyWindows(IRootDock root)
    {
        foreach (var window in root.Windows?.ToList() ?? [])
        {
            if (window.Layout is { } layout && ContainsTool(layout))
            {
                continue;
            }

            if (window.Layout is IRootDock { HiddenDockables: { } hidden })
            {
                root.HiddenDockables ??= new System.Collections.ObjectModel.ObservableCollection<IDockable>();
                foreach (var tool in hidden.OfType<ITool>())
                {
                    tool.Owner = root;
                    tool.OriginalOwner = null;
                    root.HiddenDockables.Add(tool);
                }
            }

            root.Windows!.Remove(window);
        }
    }

    private static bool ContainsTool(IDockable dockable) => dockable switch
    {
        ITool => true,
        IDock dock => dock.VisibleDockables?.Any(ContainsTool) == true,
        _ => false,
    };

    private static bool ContainsDocumentDock(IDockable dockable) => dockable switch
    {
        IDocumentDock => true,
        IDock dock => dock.VisibleDockables?.Any(ContainsDocumentDock) == true,
        _ => false,
    };

    private static void RemoveDocuments(IDockable dockable)
    {
        if (dockable is not IDock dock)
        {
            return;
        }

        if (dock is IDocumentDock && dock.VisibleDockables is { } docs)
        {
            foreach (var doc in docs.Where(d => d is not ITool).ToList())
            {
                docs.Remove(doc);
            }

            dock.ActiveDockable = docs.FirstOrDefault();
        }

        foreach (var child in dock.VisibleDockables ?? [])
        {
            RemoveDocuments(child);
        }

        if (dock is IRootDock root)
        {
            foreach (var window in root.Windows ?? [])
            {
                if (window.Layout is { } windowLayout)
                {
                    RemoveDocuments(windowLayout);
                }
            }
        }
    }
}
