using Avalonia.Controls;
using Avalonia.Input;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Views.Tools;

public partial class FileTreeToolView : UserControl
{
    public FileTreeToolView() => InitializeComponent();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Open();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Open();
            e.Handled = true;
        }
    }

    private void Open()
    {
        if (DataContext is FileTreeToolViewModel vm)
        {
            vm.OpenCommand.Execute(vm.SelectedNode);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // Clicking a folder opens or closes it.
        if (e.InitialPressMouseButton == MouseButton.Left && DataContext is FileTreeToolViewModel { SelectedNode: { IsFolder: true } folder })
        {
            folder.IsExpanded = !folder.IsExpanded;
            return;
        }

        // Files open on a single click, like Inky.
        if (e.InitialPressMouseButton == MouseButton.Left && DataContext is FileTreeToolViewModel { SelectedNode: { IsHeader: false, IsFolder: false } } vm)
        {
            vm.OpenCommand.Execute(vm.SelectedNode);
        }
    }
}
