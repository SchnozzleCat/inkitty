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
    public const int LayoutVersion = 1;

    private const string FileName = "layout.json";
    private const string VersionFileName = "layout.version";

    private readonly DockSerializer _serializer = new(typeof(System.Collections.ObjectModel.ObservableCollection<>));

    public string LayoutPath => Path.Join(configDirectory, FileName);

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
            if (!ReattachTools(layout, factory))
            {
                logger.LogWarning("Saved layout is missing tools; using default layout");
                return null;
            }

            return layout;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException or InvalidOperationException or InvalidCastException)
        {
            logger.LogWarning(ex, "Could not restore dock layout; using default layout");
            return null;
        }
    }

    public void Save(IRootDock layout)
    {
        try
        {
            Directory.CreateDirectory(configDirectory);
            var json = _serializer.Serialize(layout);

            // Strip open documents from the saved copy: deserialise, prune, re-serialise.
            var copy = _serializer.Deserialize<IRootDock?>(json);
            if (copy is not null)
            {
                RemoveDocuments(copy);
                json = _serializer.Serialize(copy);
            }

            File.WriteAllText(LayoutPath, json);
            File.WriteAllText(Path.Join(configDirectory, VersionFileName), LayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not save dock layout");
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

        Visit(root.VisibleDockables);
        Visit(root.HiddenDockables);
        Visit(root.LeftPinnedDockables);
        Visit(root.RightPinnedDockables);
        Visit(root.TopPinnedDockables);
        Visit(root.BottomPinnedDockables);
        foreach (var window in root.Windows ?? [])
        {
            if (window.Layout is { } windowLayout)
            {
                Visit(windowLayout.VisibleDockables);
            }
        }

        // Tools missing entirely from the saved layout get re-added via View menu; still require the
        // document dock to exist so the editor has somewhere to live.
        return ContainsDocumentDock(root) && found.Count > 0;
    }

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
