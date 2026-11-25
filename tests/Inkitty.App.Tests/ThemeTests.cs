using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Inkitty.App.Services;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.Theming;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class ThemeTests
{
    private static Color ColorOf(string key)
    {
        var app = Application.Current!;
        app.TryGetResource(key, app.ActualThemeVariant, out var value);
        return value switch
        {
            ISolidColorBrush brush => brush.Color,
            Color color => color,
            _ => throw new InvalidOperationException($"{key} not found"),
        };
    }

    // Window, Dock panes, editor surfaces, open menus, menu/status bar.
    private static readonly string[] Surfaces =
    [
        "SystemRegionBrush",
        "RegionColor",
        "SystemControlBackgroundAltHighBrush",
        "MenuFlyoutPresenterBackground",
        "InkittyChromeBackground",
    ];

    public static IEnumerable<string> ThemeIds() => ThemeCatalog.Themes.Select(t => t.Id);

    [Test]
    public async Task Catalog_has_light_and_dark_themes_with_unique_ids()
    {
        await Assert.That(ThemeCatalog.Themes.Count).IsGreaterThanOrEqualTo(15);
        await Assert.That(ThemeCatalog.Themes.Select(t => t.Id).Distinct().Count()).IsEqualTo(ThemeCatalog.Themes.Count);
        await Assert.That(ThemeCatalog.Themes.Any(t => t.IsDark)).IsTrue();
        await Assert.That(ThemeCatalog.Themes.Any(t => !t.IsDark)).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(ThemeIds))]
    public async Task Every_theme_gives_a_coherent_window(string id) =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { ColorTheme = id }));
            var theme = ThemeCatalog.Find(id)!;

            await Assert.That(Application.Current!.ActualThemeVariant).IsEqualTo(theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light);

            // All surfaces are on the theme's side of the light/dark divide and close to each other...
            var lums = Surfaces.Select(k => ColorMath.Luminance(ColorOf(k))).ToList();
            if (theme.IsDark)
            {
                await Assert.That(lums.Max()).IsLessThan(0.45);
            }
            else
            {
                await Assert.That(lums.Min()).IsGreaterThan(0.55);
            }

            await Assert.That(lums.Max() - lums.Min()).IsLessThan(0.25);

            // ...and text is readable on them.
            var text = ColorMath.Luminance(ColorOf("SystemControlForegroundBaseHighBrush"));
            await Assert.That(Math.Abs(text - lums.Average())).IsGreaterThan(0.35);
        });

    [Test]
    public async Task Switching_theme_at_runtime_updates_window_and_editor() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { ColorTheme = "light-plus" }));
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var editor = app.EditorFor(app.Shell.ActiveDocument!)!;
            var lightRegion = ColorOf("RegionColor");

            app.Settings.Update(s => s with { ColorTheme = "dracula" });
            await UiTestHelpers.WaitUntil(() => editor.SyntaxTheme == TextMateSharp.Grammars.ThemeName.Dracula);

            await Assert.That(app.Get<ThemeService>().Current!.Id).IsEqualTo("dracula");
            await Assert.That(ColorOf("RegionColor")).IsNotEqualTo(lightRegion);
            await Assert.That(editor.SyntaxTheme).IsEqualTo(TextMateSharp.Grammars.ThemeName.Dracula);
            await Assert.That(((ISolidColorBrush)editor.TextEditor.Background!).Color).IsEqualTo(Color.Parse("#282a36"));
        });

    [Test]
    public async Task Unknown_theme_falls_back_to_light_or_dark_plus() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { ColorTheme = "no-such-theme" }));
            var current = app.Get<ThemeService>().Current!;

            await Assert.That(current.Id is ThemeCatalog.LightPlusId or ThemeCatalog.DarkPlusId).IsTrue();
        });
}
