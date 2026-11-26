using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Messages;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;

namespace Inkitty.App.ViewModels.Tools;

public enum FileNodeKind
{
    Folder,
    File,
    Header,
    Unincluded,
}

/// <summary>A node in the files tree: a folder, a file, or a group header.</summary>
public sealed partial class FileNode : ObservableObject
{
    /// <param name="file">Project-relative file (for folders, the folder's path).</param>
    public FileNode(string label, FileNodeKind kind, string? file)
    {
        Label = label;
        Kind = kind;
        File = kind == FileNodeKind.Folder ? null : file;
        FolderPath = kind == FileNodeKind.Folder ? file : null;
    }

    public string Label { get; }

    /// <summary>Project-relative path of a folder node (e.g. <c>chapters</c>).</summary>
    public string? FolderPath { get; }

    /// <summary>Identifies the node across rebuilds, to keep it expanded or selected.</summary>
    public string Key => Kind switch
    {
        FileNodeKind.Folder => $"folder:{FolderPath}",
        FileNodeKind.Header => $"header:{Label}",
        _ => $"file:{File}",
    };

    public FileNodeKind Kind { get; }

    /// <summary>Project-relative file the node belongs to (null for headers).</summary>
    public string? File { get; }


    public ObservableCollection<FileNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool HasErrors { get; set; }

    public bool IsFile => Kind == FileNodeKind.File;

    public bool IsFolder => Kind == FileNodeKind.Folder;

    public bool IsHeader => Kind == FileNodeKind.Header;

    public bool IsUnincluded => Kind == FileNodeKind.Unincluded;

}

/// <summary>
/// The files tool: project files grouped by folder (folders first, then files in include order).
/// The active file's structure is in the Outline pane.
/// </summary>
public sealed partial class FileTreeToolViewModel : Tool
{
    private readonly IMessenger _messenger;
    private ProjectViewModel? _project;

    /// <summary>Folders start expanded; remember the ones the user collapsed.</summary>
    private readonly HashSet<string> _collapsedFolders = [];

    public FileTreeToolViewModel(IMessenger messenger)
    {
        _messenger = messenger;
        Id = DockIds.Files;
        Title = "Files";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
    }

    public ObservableCollection<FileNode> Nodes { get; } = [];

    [ObservableProperty]
    public partial FileNode? SelectedNode { get; set; }

    [ObservableProperty]
    public partial string ProjectName { get; private set; } = string.Empty;

    public void Attach(ProjectViewModel? project)
    {
        if (_project is not null)
        {
            _project.Files.CollectionChanged -= OnStructureChanged;
            _project.UnincludedFiles.CollectionChanged -= OnStructureChanged;
            _project.Compiled -= OnCompiled;
            foreach (var file in _project.Project.Files)
            {
                file.Changed -= OnFileChanged;
            }
        }

        _project = project;
        _collapsedFolders.Clear();
        ProjectName = project?.Name ?? string.Empty;
        if (project is not null)
        {
            project.Files.CollectionChanged += OnStructureChanged;
            project.UnincludedFiles.CollectionChanged += OnStructureChanged;
            project.Compiled += OnCompiled;
        }

        Rebuild();
    }

    /// <summary>Opens the node's file.</summary>
    [RelayCommand]
    private void Open(FileNode? node)
    {
        node ??= SelectedNode;
        if (node?.File is not { } file)
        {
            return;
        }

        if (node.IsUnincluded)
        {
            _messenger.Send(new StatusMessage($"{file} is not included from {_project?.Project.MainFile}. Add 'INCLUDE {file}' to use it."));
            return;
        }

        _messenger.Send(new NavigateToSourceMessage(file, null));
    }

    private void OnStructureChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnCompiled(object? sender, Core.Compilation.CompileResult e) => Rebuild();

    private void OnFileChanged(object? sender, EventArgs e)
    {
        if (sender is InkSourceFile file && Flatten(Nodes).FirstOrDefault(n => n.IsFile && n.File == file.RelativePath) is { } node)
        {
            node.IsDirty = file.IsDirty;
        }
    }

    private void Rebuild()
    {
        var all = Flatten(Nodes).ToList();
        foreach (var folder in all.Where(n => n.IsFolder))
        {
            if (folder.IsExpanded)
            {
                _collapsedFolders.Remove(folder.Key);
            }
            else
            {
                _collapsedFolders.Add(folder.Key);
            }
        }

        var selected = SelectedNode?.Key;
        Nodes.Clear();
        if (_project is null)
        {
            return;
        }

        var errorFiles = _project.Issues.Where(i => i.Severity.IsError).Select(i => i.File).ToHashSet();
        List<FileNode> files = [];
        foreach (var file in _project.Project.Files)
        {
            file.Changed -= OnFileChanged;
            file.Changed += OnFileChanged;
            var node = new FileNode(FileName(file.RelativePath), FileNodeKind.File, file.RelativePath)
            {
                IsDirty = file.IsDirty,
                HasErrors = errorFiles.Contains(file.RelativePath),
            };

            files.Add(node);
        }

        foreach (var node in GroupByFolder(files))
        {
            Nodes.Add(node);
        }

        if (_project.UnincludedFiles.Count > 0)
        {
            var header = new FileNode("Not included", FileNodeKind.Header, null) { IsExpanded = true };
            var unincluded = _project.UnincludedFiles.Select(path => new FileNode(FileName(path), FileNodeKind.Unincluded, path));
            foreach (var node in GroupByFolder(unincluded, keyPrefix: "unincluded/"))
            {
                header.Children.Add(node);
            }

            Nodes.Add(header);
        }

        SelectedNode = selected is null ? null : Flatten(Nodes).FirstOrDefault(n => n.Key == selected);
    }

    private static string FileName(string relativePath) => relativePath[(relativePath.LastIndexOf('/') + 1)..];

    /// <summary>
    /// Nests file nodes under folder nodes by their path. Folders come first (by name), then files in the
    /// order given (include order, so the main file leads the root).
    /// </summary>
    private List<FileNode> GroupByFolder(IEnumerable<FileNode> files, string keyPrefix = "")
    {
        var root = new FileNode(string.Empty, FileNodeKind.Folder, keyPrefix);
        Dictionary<string, FileNode> folders = [];
        foreach (var file in files)
        {
            var parent = root;
            var segments = file.File!.Split('/');
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var path = keyPrefix + string.Join('/', segments[..(i + 1)]);
                if (!folders.TryGetValue(path, out var folder))
                {
                    folder = folders[path] = new FileNode(segments[i], FileNodeKind.Folder, path);
                    folder.IsExpanded = !_collapsedFolders.Contains(folder.Key);
                    parent.Children.Add(folder);
                }

                parent = folder;
            }

            parent.Children.Add(file);
        }

        foreach (var folder in folders.Values.Prepend(root))
        {
            List<FileNode> ordered = [.. folder.Children.Where(c => c.IsFolder).OrderBy(c => c.Label, StringComparer.OrdinalIgnoreCase), .. folder.Children.Where(c => !c.IsFolder)];
            folder.Children.Clear();
            foreach (var child in ordered)
            {
                folder.Children.Add(child);
            }
        }

        // A folder shows red if any file inside it has errors.
        foreach (var folder in folders.Values.OrderByDescending(f => f.FolderPath!.Length))
        {
            folder.HasErrors = folder.Children.Any(c => c.HasErrors);
        }

        return [.. root.Children];
    }

    private static IEnumerable<FileNode> Flatten(IEnumerable<FileNode> nodes) =>
        nodes.SelectMany(n => (IEnumerable<FileNode>)[n, .. Flatten(n.Children)]);
}
