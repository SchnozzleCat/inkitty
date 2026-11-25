using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Inkitty.App.Tests.Infrastructure;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class ThemeTests
{
    private static Color ColorOf(string key, ThemeVariant variant)
    {
        Application.Current!.TryGetResource(key, variant, out var value);
        return value switch
        {
            ISolidColorBrush brush => brush.Color,
            Color color => color,
            _ => throw new InvalidOperationException($"{key} not found"),
        };
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    [Test]
    public async Task Dark_theme_surfaces_are_one_family_of_dark_greys() =>
        await HeadlessSession.Run(async () =>
        {
            var dark = ThemeVariant.Dark;
            string[] surfaces =
            [
                "SystemRegionBrush",                        // window
                "RegionColor",                              // Dock panes
                "SystemControlBackgroundAltHighBrush",      // editor-like surfaces
                "MenuFlyoutPresenterBackground",            // open menus
                "SystemControlBackgroundChromeMediumBrush", // menu bar / status bar
            ];

            var lums = surfaces.Select(k => Luminance(ColorOf(k, dark))).ToList();

            // Nothing pure black, nothing bright, and all within a narrow band.
            await Assert.That(lums.Min()).IsGreaterThan(0.05);
            await Assert.That(lums.Max()).IsLessThan(0.25);
            await Assert.That(lums.Max() - lums.Min()).IsLessThan(0.08);
        });
}
