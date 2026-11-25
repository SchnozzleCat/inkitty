using Inkitty.App.Controls.InkEditor;
using Inkitty.App.ViewModels;
using Inkitty.App.ViewModels.Documents;
using Inkitty.App.Views;
using Inkitty.Core.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Inkitty.App.Tests.Infrastructure;

/// <summary>
/// A fresh main window + view model per test (services are built per fixture, so tests don't share state).
/// Must be created and used on the headless UI thread.
/// </summary>
public sealed class AppFixture : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private AppFixture(ServiceProvider services, string configDirectory)
    {
        _services = services;
        ConfigDirectory = configDirectory;
        Shell = services.GetRequiredService<MainWindowViewModel>();
        Window = new MainWindow { DataContext = Shell };
        Window.Show();
    }

    public string ConfigDirectory { get; }

    public MainWindowViewModel Shell { get; }

    public MainWindow Window { get; }

    public FakeDialogService Dialogs => (FakeDialogService)_services.GetRequiredService<Services.IDialogService>();

    public ISettingsStore Settings => _services.GetRequiredService<ISettingsStore>();

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    public ProjectViewModel Project => Shell.Project ?? throw new InvalidOperationException("No project open");

    public static AppFixture Create(string? configDirectory = null, bool watchFiles = false, Action<ISettingsStore>? configure = null)
    {
        configDirectory ??= Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
        var services = App.ConfigureServices(new AppOptions
        {
            ConfigDirectory = configDirectory,
            WatchFiles = watchFiles,
            Dialogs = new FakeDialogService(),
        });
        configure?.Invoke(services.GetRequiredService<ISettingsStore>());
        services.GetRequiredService<Services.ThemeService>().ApplyCurrent(); // as App startup does
        return new AppFixture(services, configDirectory);
    }

    /// <summary>Opens a project and waits for the first compile and replay.</summary>
    public async Task OpenAsync(string mainFile)
    {
        await Shell.OpenProjectAsync(mainFile);
        await UiTestHelpers.WaitUntil(() => Shell.Project?.LastResult is not null && !Shell.Factory.PlayerTool.IsBusy);
    }

    public InkEditor? EditorFor(EditorDocumentViewModel document) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(Window).OfType<InkEditor>().FirstOrDefault(e => e.DataContext == document);

    /// <summary>Waits for the compile triggered by an edit (debounce + compile + replay).</summary>
    public async Task<bool> WaitForCompileAfter(Action edit)
    {
        var before = Shell.Project?.LastResult;
        edit();
        return await UiTestHelpers.WaitUntil(() => Shell.Project?.LastResult != before && !Shell.Factory.PlayerTool.IsBusy);
    }

    public async ValueTask DisposeAsync()
    {
        Dialogs.UnsavedChoice = Services.UnsavedChangesChoice.Discard;
        await Shell.CloseProjectAsync();
        Window.Close();
        await _services.DisposeAsync();
    }
}
