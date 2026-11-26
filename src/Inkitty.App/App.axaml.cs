using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Messaging;
using Inkitty.App.Docking;
using Inkitty.App.Services;
using Inkitty.App.ViewModels;
using Inkitty.App.ViewModels.Tools;
using Inkitty.App.Views;
using Inkitty.Core.Compilation;
using Inkitty.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Inkitty.App;

public partial class App : Application
{
    /// <summary>The service provider (composition root). Null before initialisation.</summary>
    public static IServiceProvider? Services { get; private set; }

    /// <summary>Options for tests: skip file watching, use a temp config dir, etc.</summary>
    public static AppOptions Options { get; set; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var services = ConfigureServices(Options);
        Services = services;
        services.GetRequiredService<ThemeService>().ApplyCurrent();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) => shell.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));

            // Command line: Inkitty path/to/main.ink
            var file = desktop.Args?.FirstOrDefault(a => a.EndsWith(".ink", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
            if (file is not null)
            {
                window.Opened += async (_, _) => await shell.OpenProjectAsync(Path.GetFullPath(file));
            }

            // Developer aid: INKITTY_SNAPSHOT=out.png renders the window to a PNG after startup.
            if (Environment.GetEnvironmentVariable("INKITTY_SNAPSHOT") is { Length: > 0 } snapshotPath)
            {
                window.Opened += async (_, _) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(4));
                    var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                    bitmap.Render(window);
                    bitmap.Save(snapshotPath, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                };
            }
        }

        // macOS: files opened from Finder / dropped on the dock icon.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += async (_, e) =>
            {
                if (e is FileActivatedEventArgs { Files: [var first, ..] } && first.TryGetLocalPath() is { } path && Services is { } sp)
                {
                    await sp.GetRequiredService<MainWindowViewModel>().OpenProjectAsync(path);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static ServiceProvider ConfigureServices(AppOptions options)
    {
        var configDirectory = options.ConfigDirectory ?? AppPaths.ConfigDirectory();
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton(options.TimeProvider ?? TimeProvider.System);
        services.AddSingleton<ISettingsStore>(sp => new SettingsStore(configDirectory, sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<SettingsStore>>()));
        services.AddSingleton<IMessenger>(_ => new WeakReferenceMessenger());
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();
        services.AddSingleton(options.Dialogs ?? new DialogService());
        services.AddSingleton<IInkCompilerService, InkCompilerService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<AutoSaveService>();
        services.AddSingleton(sp => new DockLayoutService(configDirectory, sp.GetRequiredService<ILogger<DockLayoutService>>()));
        services.AddSingleton<FileTreeToolViewModel>();
        services.AddSingleton<IssuesToolViewModel>();
        services.AddSingleton(sp => new PlayerToolViewModel(sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<ISettingsStore>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<StoryMapToolViewModel>();
        services.AddSingleton<VariablesToolViewModel>();
        services.AddSingleton(sp => new OutlineToolViewModel(sp.GetRequiredService<IMessenger>(), sp.GetRequiredService<IUiDispatcher>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<InkittyDockFactory>();
        services.AddSingleton(sp => new MainWindowViewModel(
            sp.GetRequiredService<InkittyDockFactory>(),
            sp.GetRequiredService<IInkCompilerService>(),
            sp.GetRequiredService<ISettingsStore>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IMessenger>(),
            sp.GetRequiredService<AutoSaveService>(),
            sp.GetRequiredService<DockLayoutService>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            options.WatchFiles,
            sp.GetRequiredService<ThemeService>()));
        services.AddTransient<SettingsViewModel>();
        return services.BuildServiceProvider();
    }
}

/// <summary>Startup options (mainly for tests).</summary>
public sealed record AppOptions
{
    public string? ConfigDirectory { get; init; }

    public bool WatchFiles { get; init; } = true;

    public IDialogService? Dialogs { get; init; }

    public TimeProvider? TimeProvider { get; init; }
}
