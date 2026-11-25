using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Inkitty.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Inkitty.Core.Settings;

namespace Inkitty.App.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        InWindowMenu.IsVisible = !OperatingSystem.IsMacOS();
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Activated += async (_, _) => await (ViewModel?.OnWindowActivatedAsync() ?? Task.CompletedTask);
        Deactivated += async (_, _) => await (ViewModel?.OnWindowDeactivatedAsync() ?? Task.CompletedTask);
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.SettingsRequested += (_, _) => _ = new SettingsWindow { DataContext = App.Services?.GetRequiredService<SettingsViewModel>() }.ShowDialog(this);
            vm.ExitRequested += (_, _) => Close();
            RegisterShortcuts(vm);
            RestoreBounds(vm.Settings.LastWindowBounds);
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || ViewModel is not { } vm)
        {
            return;
        }

        // Ask asynchronously, then close for real.
        e.Cancel = true;
        SaveBounds();
        if (await vm.OnClosingAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void RegisterShortcuts(MainWindowViewModel vm)
    {
        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        KeyBindings.Clear();
        void Bind(Key key, KeyModifiers modifiers, System.Windows.Input.ICommand cmd) =>
            KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = cmd });

        Bind(Key.N, command, vm.NewProjectCommand);
        Bind(Key.O, command, vm.OpenCommand);
        Bind(Key.S, command, vm.SaveCommand);
        Bind(Key.S, command | KeyModifiers.Shift, vm.SaveAllCommand);
        Bind(Key.W, command, vm.CloseDocumentCommand);
        Bind(Key.R, command, vm.RestartStoryCommand);
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

    private void RestoreBounds(WindowBounds? bounds)
    {
        if (bounds is null || bounds.Width < MinWidth || bounds.Height < MinHeight)
        {
            return;
        }

        Width = bounds.Width;
        Height = bounds.Height;

        // Only restore the position if it is on a connected screen (monitors may have changed).
        var point = new PixelPoint((int)bounds.X, (int)bounds.Y);
        if (Screens.All.Any(s => s.WorkingArea.Contains(point)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = point;
        }

        if (bounds.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SaveBounds()
    {
        if (App.Services?.GetService<ISettingsStore>() is not { } store)
        {
            return;
        }

        var maximized = WindowState == WindowState.Maximized;
        var bounds = maximized && store.Current.LastWindowBounds is { } previous
            ? previous with { IsMaximized = true }
            : new WindowBounds(Position.X, Position.Y, Width, Height, maximized);
        store.Update(s => s with { LastWindowBounds = bounds });
    }

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
