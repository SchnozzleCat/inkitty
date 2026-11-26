using Avalonia.Controls;
using Avalonia.Input;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Views.Tools;

public partial class VariablesToolView : UserControl
{
    public VariablesToolView() => InitializeComponent();

    // Double-click (or Enter) jumps to the declaration; a single click only selects, so values can be read.
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
        if (DataContext is VariablesToolViewModel vm)
        {
            vm.OpenCommand.Execute(vm.SelectedItem);
        }
    }
}
