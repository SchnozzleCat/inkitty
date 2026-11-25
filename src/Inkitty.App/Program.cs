using Avalonia;
using Avalonia.Media.Fonts;

namespace Inkitty.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .ConfigureFonts(ConfigureFonts)
            .LogToTrace();

        // Avalonia's native Wayland backend is still experimental, so X11/XWayland stays the default.
        // Use native Wayland when there is no X server to fall back to, or when asked explicitly.
        if (OperatingSystem.IsLinux() && UseNativeWayland())
        {
            builder = builder.UseWayland();
        }

        return builder;
    }

    /// <summary>Registers the bundled fonts (JetBrains Mono for the editor).</summary>
    public static void ConfigureFonts(Avalonia.Media.FontManager fonts) =>
        fonts.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts:Inkitty", UriKind.Absolute),
            new Uri("avares://Inkitty/Assets/Fonts/JetBrainsMono", UriKind.Absolute)));

    private static bool UseNativeWayland()
    {
        var forced = Environment.GetEnvironmentVariable("INKITTY_WAYLAND");
        if (forced is "0" or "false")
        {
            return false;
        }

        var hasWayland = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        var hasX11 = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        return hasWayland && (forced is "1" or "true" || !hasX11);
    }
}
