using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Views.Tools;

public partial class OutlineToolView : UserControl
{
    public OutlineToolView() => InitializeComponent();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Open();
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // A click jumps to the element (selection alone also changes as the caret moves, so it can't).
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual source
            && source.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { DataContext: OutlineNode })
        {
            Open();
        }
    }

    private void Open()
    {
        if (DataContext is OutlineToolViewModel vm)
        {
            vm.OpenCommand.Execute(vm.SelectedNode);
        }
    }
}
