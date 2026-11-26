using Avalonia;
using Avalonia.Media.Fonts;

namespace Inkitty.App;

/// <summary>App builder setup shared by the desktop and browser hosts.</summary>
public static class InkittyAppBuilder
{
    /// <summary>Configures the app (fonts, logging); the host adds its platform.</summary>
    public static AppBuilder Configure() =>
        AppBuilder.Configure<App>()
            .WithInterFont()
            .ConfigureFonts(ConfigureFonts)
            .LogToTrace();

    /// <summary>Registers the bundled fonts (JetBrains Mono for the editor).</summary>
    public static void ConfigureFonts(Avalonia.Media.FontManager fonts) =>
        fonts.AddFontCollection(new EmbeddedFontCollection(
            new Uri("fonts:Inkitty", UriKind.Absolute),
            new Uri("avares://Inkitty.App/Assets/Fonts/JetBrainsMono", UriKind.Absolute)));
}
