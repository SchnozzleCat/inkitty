using Avalonia;
using Avalonia.Media.Fonts;
using Dock.Model.Core;
using Dock.Settings;

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

        if (UseManagedDockWindows())
        {
            builder = builder.UseFloatingWindowHostMode(DockFloatingWindowHostMode.Managed);
        }

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

    /// <summary>
    /// On Linux, floating panels and the drag preview are drawn inside the main window instead of as
    /// separate OS windows. Tiling window managers (Hyprland, Sway, i3, ...) map, tile or focus those
    /// extra windows, which cancels the drag the moment it starts. INKITTY_NATIVE_DOCK_WINDOWS=1 opts out.
    /// </summary>
    private static bool UseManagedDockWindows()
    {
        var native = Environment.GetEnvironmentVariable("INKITTY_NATIVE_DOCK_WINDOWS");
        return native switch
        {
            "1" or "true" => false,
            "0" or "false" => true,
            _ => OperatingSystem.IsLinux(),
        };
    }

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
