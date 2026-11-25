using Avalonia.Controls;
using Avalonia.Input;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Views.Tools;

public partial class IssuesToolView : UserControl
{
    public IssuesToolView() => InitializeComponent();

    private void OnDoubleTapped(object? sender, TappedEventArgs e) => Open();

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
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            Open();
        }
    }

    private void Open()
    {
        if (DataContext is IssuesToolViewModel vm)
        {
            vm.OpenCommand.Execute(vm.SelectedItem);
        }
    }
}
