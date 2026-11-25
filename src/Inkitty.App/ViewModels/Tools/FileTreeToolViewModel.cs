using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Messages;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;
using Inkitty.Core.Symbols;

namespace Inkitty.App.ViewModels.Tools;

public enum FileNodeKind
{
    File,
    Knot,
    Stitch,
    Function,
    Header,
    Unincluded,
}

/// <summary>A node in the files tree: a file, a knot/stitch outline entry, or a group header.</summary>
public sealed partial class FileNode : ObservableObject
{
    public FileNode(string label, FileNodeKind kind, string? file, SourceRange? range = null)
    {
        Label = label;
        Kind = kind;
        File = file;
        Range = range;
    }

    public string Label { get; }

    public FileNodeKind Kind { get; }

    /// <summary>Project-relative file the node belongs to (null for headers).</summary>
    public string? File { get; }

    public SourceRange? Range { get; }

    public ObservableCollection<FileNode> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool HasErrors { get; set; }

    public bool IsFile => Kind == FileNodeKind.File;

    public bool IsHeader => Kind == FileNodeKind.Header;

    public bool IsUnincluded => Kind == FileNodeKind.Unincluded;

    public bool IsOutline => Kind is FileNodeKind.Knot or FileNodeKind.Stitch or FileNodeKind.Function;

    public string Icon => Kind switch
    {
        FileNodeKind.Knot => "==",
        FileNodeKind.Stitch => "=",
        FileNodeKind.Function => "ƒ",
        _ => string.Empty,
    };
}

/// <summary>The files tool: the include tree, with a knot/stitch outline under each file.</summary>
public sealed partial class FileTreeToolViewModel : Tool
{
    private readonly IMessenger _messenger;
    private ProjectViewModel? _project;

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
        ProjectName = project?.Name ?? string.Empty;
        if (project is not null)
        {
            project.Files.CollectionChanged += OnStructureChanged;
            project.UnincludedFiles.CollectionChanged += OnStructureChanged;
            project.Compiled += OnCompiled;
        }

        Rebuild();
    }

    /// <summary>Opens the node's file (and jumps to the outline entry).</summary>
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

        _messenger.Send(new NavigateToSourceMessage(file, node.Range));
    }

    private void OnStructureChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => Rebuild();

    private void OnCompiled(object? sender, Core.Compilation.CompileResult e) => Rebuild();

    private void OnFileChanged(object? sender, EventArgs e)
    {
        if (sender is InkSourceFile file && Nodes.FirstOrDefault(n => n.File == file.RelativePath) is { } node)
        {
            node.IsDirty = file.IsDirty;
        }
    }

    private void Rebuild()
    {
        var expanded = Nodes.Where(n => n.IsExpanded).Select(n => n.File).ToHashSet();
        var selected = SelectedNode is { } s ? (s.File, s.Label) : default;
        Nodes.Clear();
        if (_project is null)
        {
            return;
        }

        var symbols = _project.LastResult?.Symbols;
        var errorFiles = _project.Issues.Where(i => i.Severity.IsError).Select(i => i.File).ToHashSet();
        foreach (var file in _project.Project.Files)
        {
            file.Changed -= OnFileChanged;
            file.Changed += OnFileChanged;
            var node = new FileNode(file.RelativePath, FileNodeKind.File, file.RelativePath)
            {
                IsDirty = file.IsDirty,
                HasErrors = errorFiles.Contains(file.RelativePath),
                IsExpanded = expanded.Contains(file.RelativePath),
            };

            if (symbols is not null)
            {
                AddOutline(node, symbols.OutlineOf(file.RelativePath));
            }

            Nodes.Add(node);
        }

        if (_project.UnincludedFiles.Count > 0)
        {
            var header = new FileNode("Not included", FileNodeKind.Header, null) { IsExpanded = true };
            foreach (var path in _project.UnincludedFiles)
            {
                header.Children.Add(new FileNode(path, FileNodeKind.Unincluded, path));
            }

            Nodes.Add(header);
        }

        SelectedNode = Flatten(Nodes).FirstOrDefault(n => n.File == selected.File && n.Label == selected.Label);
    }

    private static void AddOutline(FileNode fileNode, IReadOnlyList<SymbolDefinition> outline)
    {
        FileNode? currentKnot = null;
        foreach (var def in outline)
        {
            switch (def.Kind)
            {
                case SymbolKind.Stitch when currentKnot is not null && def.Container == currentKnot.Label:
                    currentKnot.Children.Add(new FileNode(def.Name, FileNodeKind.Stitch, def.Range.File, def.Range));
                    break;
                case SymbolKind.Function:
                    currentKnot = null;
                    fileNode.Children.Add(new FileNode(def.Name, FileNodeKind.Function, def.Range.File, def.Range));
                    break;
                case SymbolKind.Stitch:
                    fileNode.Children.Add(new FileNode(def.QualifiedName, FileNodeKind.Stitch, def.Range.File, def.Range));
                    break;
                default:
                    currentKnot = new FileNode(def.Name, FileNodeKind.Knot, def.Range.File, def.Range);
                    fileNode.Children.Add(currentKnot);
                    break;
            }
        }
    }

    private static IEnumerable<FileNode> Flatten(IEnumerable<FileNode> nodes) =>
        nodes.SelectMany(n => (IEnumerable<FileNode>)[n, .. Flatten(n.Children)]);
}
