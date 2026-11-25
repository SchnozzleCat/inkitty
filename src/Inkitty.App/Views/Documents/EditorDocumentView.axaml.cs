using Avalonia;
using Avalonia.Controls;
using Inkitty.App.ViewModels;

namespace Inkitty.App.Views.Documents;

public partial class EditorDocumentView : UserControl
{
    public EditorDocumentView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Editor settings come from the shell; bind them here rather than threading them through every document VM.
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel shell)
        {
            Editor.Settings = shell.Settings;
            shell.PropertyChanged -= OnShellPropertyChanged;
            shell.PropertyChanged += OnShellPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (TopLevel.GetTopLevel(this)?.DataContext is MainWindowViewModel shell)
        {
            shell.PropertyChanged -= OnShellPropertyChanged;
        }
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.Settings) && sender is MainWindowViewModel shell)
        {
            Editor.Settings = shell.Settings;
        }
    }
}
