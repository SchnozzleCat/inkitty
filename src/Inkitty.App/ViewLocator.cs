using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Core;
using Inkitty.App.ViewModels.Documents;
using Inkitty.App.ViewModels.Tools;
using Inkitty.App.Views.Documents;
using Inkitty.App.Views.Tools;

namespace Inkitty.App;

/// <summary>Maps dockable view models to views. Explicit (no reflection), so it is trimming/AOT-friendly.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        EditorDocumentDockable => new EditorDocumentView(),
        FileTreeToolViewModel => new FileTreeToolView(),
        PlayerToolViewModel => new PlayerToolView(),
        IssuesToolViewModel => new IssuesToolView(),
        StoryMapToolViewModel => new StoryMapToolView(),
        VariablesToolViewModel => new VariablesToolView(),
        OutlineToolViewModel => new OutlineToolView(),
        _ => new TextBlock { Text = $"No view for {param?.GetType().Name}" },
    };

    public bool Match(object? data) => data is IDockable;
}
