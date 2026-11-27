using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Inkitty.App.Views;

/// <summary>The settings dialog content (shown modally by <see cref="MainView"/>).</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>Raised when the user clicks Close.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the user asks to edit the keyboard shortcuts (shown on top: modals stack).</summary>
    public event EventHandler? EditShortcutsRequested;

    private void OnEditShortcuts(object? sender, RoutedEventArgs e) => EditShortcutsRequested?.Invoke(this, EventArgs.Empty);

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
