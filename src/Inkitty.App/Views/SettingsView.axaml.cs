using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Inkitty.App.Views;

/// <summary>The settings dialog content (shown modally by <see cref="MainView"/>).</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Raised when the user clicks Close.</summary>
    public event EventHandler? CloseRequested;

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
