using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Input;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Views.Tools;

public partial class OutlineToolView : UserControl
{
    public OutlineToolView() => InitializeComponent();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var node = (DataContext as OutlineToolViewModel)?.SelectedNode;
        switch (e.Key)
        {
            case Key.Enter:
                Open();
                e.Handled = true;
                break;

            // As in a tree: Right opens (or steps into), Left closes (or steps out).
            case Key.Right when node is { HasChildren: true, IsExpanded: false }:
                node.IsExpanded = true;
                e.Handled = true;
                break;
            case Key.Left when node is { HasChildren: true, IsExpanded: true }:
                node.IsExpanded = false;
                e.Handled = true;
                break;
            case Key.Left when node?.Parent is { } parent && DataContext is OutlineToolViewModel vm:
                vm.SelectedNode = parent;
                e.Handled = true;
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // A click jumps to the element (selection alone also changes as the caret moves, so it can't).
        // Clicking the chevron only folds.
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Avalonia.Visual source
            && source.FindAncestorOfType<ToggleButton>(includeSelf: true) is null
            && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: OutlineNode })
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
