using Avalonia;
using Avalonia.Controls;
using Inkitty.App.ViewModels;
using Inkitty.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Inkitty.App.Views;

/// <summary>Desktop host for <see cref="MainView"/>: native menu, window bounds, close confirmation, focus events.</summary>
public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Activated += async (_, _) => await (ViewModel?.OnWindowActivatedAsync() ?? Task.CompletedTask);
        Deactivated += async (_, _) => await (ViewModel?.OnWindowDeactivatedAsync() ?? Task.CompletedTask);
    }

    /// <summary>The hosted IDE view.</summary>
    public MainView MainView => View;

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ViewModel is { } vm)
        {
            vm.ExitRequested += (_, _) => Close();
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
}
