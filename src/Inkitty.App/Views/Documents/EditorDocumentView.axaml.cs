using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Inkitty.App.ViewModels;

namespace Inkitty.App.Views.Documents;

public partial class EditorDocumentView : UserControl
{
    private MainWindowViewModel? _shell;

    public EditorDocumentView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Editor settings come from the shell; bind them here rather than threading them through every document VM.
        if (FindShell() is { } shell)
        {
            Unsubscribe();
            _shell = shell;
            Editor.Settings = shell.Settings;
            Editor.SyntaxTheme = shell.SyntaxTheme;
            shell.PropertyChanged += OnShellPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (_shell is not null)
        {
            _shell.PropertyChanged -= OnShellPropertyChanged;
            _shell = null;
        }
    }

    /// <summary>
    /// The shell view model. On desktop it's the window's DataContext; in the browser the top level has
    /// none (the shell is the DataContext of <see cref="MainView"/>), so look through the ancestors.
    /// </summary>
    private MainWindowViewModel? FindShell() =>
        this.GetVisualAncestors().OfType<Control>().Select(c => c.DataContext).OfType<MainWindowViewModel>().FirstOrDefault()
        ?? TopLevel.GetTopLevel(this)?.DataContext as MainWindowViewModel;

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel shell)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.Settings))
        {
            Editor.Settings = shell.Settings;
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SyntaxTheme))
        {
            Editor.SyntaxTheme = shell.SyntaxTheme;
        }
    }
}
