using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Inkitty.App.Services;
using Inkitty.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Inkitty.App.Views;

/// <summary>
/// The whole IDE: menu, dock layout and status bar. Hosted by <see cref="MainWindow"/> on desktop
/// and directly as the single view in the browser.
/// </summary>
public partial class MainView : UserControl, IModalHost
{
    private TopLevel? _topLevel;

    public MainView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>The view currently attached to a top level (the modal host for dialogs).</summary>
    public static MainView? Current { get; private set; }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>
    /// Shows <paramref name="content"/> modally: as a dialog window when hosted in a window (and
    /// <see cref="UseDialogWindows"/>), otherwise as an overlay inside this view. Modals stack.
    /// Completes when the dialog closes (<see cref="CloseModal"/>, Esc, or the window closing).
    /// </summary>
    public async Task ShowModalAsync(string title, Control content)
    {
        if (UseDialogWindows && TopLevel.GetTopLevel(this) is Window window)
        {
            var dialog = new Window
            {
                Title = title,
                Content = content,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            var owner = _modals.Count > 0 && _modals.Peek().Window is { } parent ? parent : window;
            var entry = new Modal(dialog, null, new TaskCompletionSource());
            _modals.Push(entry);
            try
            {
                await dialog.ShowDialog(owner);
            }
            finally
            {
                Remove(entry);
            }

            return;
        }

        var previousFocus = _topLevel?.FocusManager?.GetFocusedElement() as IInputElement;
        var card = new Border
        {
            Classes = { "modal-card" },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new DockPanel
            {
                Children =
                {
                    new TextBlock { Text = title, Classes = { "modal-title" }, [DockPanel.DockProperty] = Avalonia.Controls.Dock.Top },
                    content,
                },
            },
        };
        var backdrop = new Border { Classes = { "modal-backdrop" }, Child = card };
        backdrop.AddHandler(KeyDownEvent, OnModalKeyDown, RoutingStrategies.Tunnel);

        // Only the top modal takes input.
        if (_modals.TryPeek(out var below) && below.Overlay is { } belowOverlay)
        {
            belowOverlay.IsEnabled = false;
        }

        var overlay = new Modal(null, backdrop, new TaskCompletionSource());
        _modals.Push(overlay);
        ModalLayer.Children.Add(backdrop);
        Shell.IsEnabled = false;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => FirstFocusable(content)?.Focus(NavigationMethod.Tab));
        try
        {
            await overlay.Closed.Task;
        }
        finally
        {
            Remove(overlay);
            ModalLayer.Children.Remove(backdrop);
            backdrop.Child = null;
            card.Child = null;
            if (_modals.TryPeek(out var top) && top.Overlay is { } topOverlay)
            {
                topOverlay.IsEnabled = true;
            }

            Shell.IsEnabled = ModalLayer.Children.Count == 0;
            previousFocus?.Focus();
        }
    }

    /// <summary>Closes the top modal (window or overlay).</summary>
    public void CloseModal()
    {
        if (!_modals.TryPeek(out var top))
        {
            return;
        }

        if (top.Window is { } window)
        {
            window.Close();
        }
        else
        {
            top.Closed.TrySetResult();
        }
    }

    /// <summary>Desktop windows show dialogs as OS windows; the browser uses overlays.</summary>
    public static bool UseDialogWindows { get; set; } = true;

    /// <summary>True while a modal is showing.</summary>
    public bool IsModalOpen => _modals.Count > 0;

    private sealed record Modal(Window? Window, Control? Overlay, TaskCompletionSource Closed);

    private readonly Stack<Modal> _modals = [];

    private void Remove(Modal modal)
    {
        // Normally the top; tolerate out-of-order completion.
        var rest = _modals.Where(m => m != modal).Reverse().ToList();
        _modals.Clear();
        foreach (var m in rest)
        {
            _modals.Push(m);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        Current = this;

        // The native macOS menu (MainWindow) replaces the in-window menu there.
        InWindowMenu.IsVisible = !(OperatingSystem.IsMacOS() && _topLevel is Window);

        // A browser tab can't be quit from inside the page.
        ExitMenuItem.IsVisible = ExitSeparator.IsVisible = _topLevel is Window;

        if (ViewModel is { } vm)
        {
            RegisterShortcuts(vm);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _topLevel?.KeyBindings.Clear();
        _topLevel = null;
        if (Current == this)
        {
            Current = null;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.SettingsRequested += (_, _) => _ = ShowSettingsAsync();
            RegisterShortcuts(vm);
        }
    }

    private async Task ShowSettingsAsync()
    {
        if (App.Services?.GetRequiredService<SettingsViewModel>() is not { } settings)
        {
            return;
        }

        var view = new SettingsView { DataContext = settings };
        view.CloseRequested += (_, _) => CloseModal();
        await ShowModalAsync("Settings", view);
    }

    /// <summary>Shortcuts are bound on the top level so they work wherever focus is.</summary>
    private void RegisterShortcuts(MainWindowViewModel vm)
    {
        if (_topLevel is null)
        {
            return;
        }

        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        var bindings = _topLevel.KeyBindings;
        bindings.Clear();
        void Bind(Key key, KeyModifiers modifiers, System.Windows.Input.ICommand cmd) =>
            bindings.Add(new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = cmd });

        Bind(Key.N, command, vm.NewProjectCommand);
        Bind(Key.O, command, vm.OpenCommand);
        Bind(Key.S, command, vm.SaveCommand);
        Bind(Key.S, command | KeyModifiers.Shift, vm.SaveAllCommand);
        Bind(Key.W, command, vm.CloseDocumentCommand);
        Bind(Key.R, command, vm.RestartStoryCommand);
        Bind(Key.J, command, vm.RevealStoryPositionCommand);
        Bind(Key.M, command | KeyModifiers.Shift, vm.ShowStoryMapCommand);
        Bind(Key.OemComma, command, vm.OpenSettingsCommand);
        Bind(Key.F8, KeyModifiers.None, vm.NextIssueCommand);
        Bind(Key.F8, KeyModifiers.Shift, vm.PreviousIssueCommand);
        if (OperatingSystem.IsMacOS())
        {
            Bind(Key.OemMinus, KeyModifiers.Control, vm.GoBackCommand);
            Bind(Key.OemMinus, KeyModifiers.Control | KeyModifiers.Shift, vm.GoForwardCommand);
        }
        else
        {
            Bind(Key.Left, KeyModifiers.Alt, vm.GoBackCommand);
            Bind(Key.Right, KeyModifiers.Alt, vm.GoForwardCommand);
        }
    }

    private void OnModalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsModalOpen)
        {
            e.Handled = true;
            CloseModal();
        }
    }

    private static IInputElement? FirstFocusable(Control root) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root)
            .OfType<InputElement>()
            .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible && c is Button { IsDefault: true })
        ?? Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root)
            .OfType<InputElement>()
            .FirstOrDefault(c => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible);

    private static void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.TryGetFiles()?.Any(f => f.Name.EndsWith(".ink", StringComparison.OrdinalIgnoreCase)) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?
            .Select(f => f.TryGetLocalPath())
            .FirstOrDefault(p => p?.EndsWith(".ink", StringComparison.OrdinalIgnoreCase) == true);
        if (path is not null && ViewModel is { } vm)
        {
            await vm.OpenProjectAsync(path);
        }
    }
}
