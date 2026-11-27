using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Inkitty.App.Input;
using Inkitty.App.ViewModels;

namespace Inkitty.App.Views;

/// <summary>The keyboard shortcuts editor (shown modally by <see cref="MainView"/>).</summary>
public partial class KeyboardShortcutsView : UserControl, IKeyCapture
{
    public KeyboardShortcutsView()
    {
        InitializeComponent();

        // Tunnel: while recording, every key goes to the recorder before the focused button sees it
        // (so Space/Enter don't click it, and Tab doesn't move focus).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Raised when the user clicks Close.</summary>
    public event EventHandler? CloseRequested;

    private KeyboardShortcutsViewModel? ViewModel => DataContext as KeyboardShortcutsViewModel;

    public bool IsCapturingKeys => ViewModel?.Recording is not null;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is { Recording: not null } vm)
        {
            vm.HandleKey(e.Key, e.KeyModifiers);
            e.Handled = true;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ViewModel?.StopRecording();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
