using Avalonia;
using Avalonia.Headless;

namespace Inkitty.App.Tests.Infrastructure;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
    {
        App.Options = new AppOptions
        {
            ConfigDirectory = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N")),
            WatchFiles = false,
            Dialogs = new FakeDialogService(),
        };

        return AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true })
            .ConfigureFonts(InkittyAppBuilder.ConfigureFonts)
            .LogToTextWriter(Console.Error, Avalonia.Logging.LogEventLevel.Warning);
    }
}
