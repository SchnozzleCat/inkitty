using System.ComponentModel;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;

namespace Inkitty.App.ViewModels.Documents;

/// <summary>The Dock tab hosting one <see cref="EditorDocumentViewModel"/>.</summary>
public sealed class EditorDocumentDockable : Document
{
    public EditorDocumentDockable(EditorDocumentViewModel editor)
    {
        Editor = editor;
        Id = DockIds.DocumentPrefix + editor.RelativePath;
        Title = editor.TabTitle;
        CanClose = true;
        CanFloat = true;
        CanPin = false;
        Context = editor;
        editor.PropertyChanged += OnEditorPropertyChanged;
    }

    public EditorDocumentViewModel Editor { get; }

    /// <summary>
    /// Asked before closing. Return false to cancel (e.g. the user picked Cancel on "save changes?").
    /// Set by the shell; Dock calls <see cref="OnClose"/> synchronously, so the shell handles the prompt
    /// itself and closes the tab again once the user has decided.
    /// </summary>
    public Func<EditorDocumentDockable, bool>? CloseGuard { get; set; }

    public override bool OnClose() => CloseGuard?.Invoke(this) ?? true;

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorDocumentViewModel.TabTitle))
        {
            Title = Editor.TabTitle;
            IsModified = Editor.IsDirty;
        }
    }
}
