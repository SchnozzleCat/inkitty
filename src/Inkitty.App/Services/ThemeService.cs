using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Inkitty.App.Theming;
using Inkitty.Core.Settings;

namespace Inkitty.App.Services;

/// <summary>
/// Applies the colour theme setting to the whole app: Avalonia's light/dark variant, the Fluent
/// palette (menus, buttons, lists), Dock's pane colours, Inkitty's own brushes and the editor.
/// </summary>
public sealed class ThemeService
{
    private readonly ISettingsStore _settings;
    private readonly ResourceDictionary _brushes = [];
    private bool _installed;

    public ThemeService(ISettingsStore settings)
    {
        _settings = settings;
        _settings.Changed += (_, s) =>
        {
            if (s.ColorTheme != Current?.Id || s.ColorTheme == ThemeCatalog.SystemId)
            {
                Apply(s.ColorTheme);
            }
        };
    }

    /// <summary>The theme in effect (resolved from "system" if needed).</summary>
    public ThemeDefinition? Current { get; private set; }

    /// <summary>Raised after a theme is applied; the editor re-installs its syntax colours.</summary>
    public event EventHandler<ThemeDefinition>? ThemeChanged;

    /// <summary>The theme for the current setting, resolving "system" via the OS colour scheme.</summary>
    public static ThemeDefinition Resolve(string? id)
    {
        if (ThemeCatalog.Find(id) is { } theme)
        {
            return theme;
        }

        var osDark = Application.Current?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
        return osDark ? ThemeCatalog.DarkPlus : ThemeCatalog.LightPlus;
    }

    public void ApplyCurrent()
    {
        if (Application.Current?.PlatformSettings is { } platform && !_installed)
        {
            platform.ColorValuesChanged += (_, _) =>
            {
                if (ThemeCatalog.Find(_settings.Current.ColorTheme) is null)
                {
                    Apply(_settings.Current.ColorTheme);
                }
            };
        }

        Apply(_settings.Current.ColorTheme);
    }

    private void Apply(string id)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var theme = Resolve(id);
        if (!_installed)
        {
            app.Resources.MergedDictionaries.Add(_brushes);
            _installed = true;
        }

        app.RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        ReplaceFluentPalette(app, theme);
        FillBrushes(theme);
        Current = theme;
        ThemeChanged?.Invoke(this, theme);
    }

    /// <summary>
    /// Fluent's palette colours don't notify on change, so a new <see cref="FluentTheme"/> replaces the old one.
    /// </summary>
    private static void ReplaceFluentPalette(Application app, ThemeDefinition theme)
    {
        var index = app.Styles.IndexOf(app.Styles.OfType<FluentTheme>().FirstOrDefault()!);
        var fluent = new FluentTheme();
        fluent.Palettes[theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light] = CreatePalette(theme);
        if (index >= 0)
        {
            app.Styles[index] = fluent;
        }
        else
        {
            app.Styles.Insert(0, fluent);
        }
    }

    private static ColorPaletteResources CreatePalette(ThemeDefinition theme)
    {
        var c = theme.Colors;
        var contrast = theme.IsDark ? Colors.White : Colors.Black;
        var opposite = theme.IsDark ? Colors.Black : Colors.White;
        return new ColorPaletteResources
        {
            Accent = c.Accent,
            RegionColor = c.Sidebar,
            AltHigh = c.Background,
            AltMediumHigh = ColorMath.WithAlpha(c.Background, 0xCC),
            AltMedium = ColorMath.WithAlpha(c.Background, 0x99),
            AltMediumLow = ColorMath.WithAlpha(c.Background, 0x66),
            AltLow = ColorMath.WithAlpha(c.Background, 0x33),
            BaseHigh = c.Foreground,
            BaseMediumHigh = ColorMath.WithAlpha(c.Foreground, 0xCC),
            BaseMedium = ColorMath.WithAlpha(c.Foreground, 0x99),
            BaseMediumLow = ColorMath.WithAlpha(c.Foreground, 0x66),
            BaseLow = ColorMath.WithAlpha(c.Foreground, 0x33),
            ChromeLow = c.Sidebar,
            ChromeMedium = c.Chrome,
            ChromeMediumLow = c.Menu,
            ChromeHigh = ColorMath.Mix(c.Chrome, contrast, 0.25),
            ChromeAltLow = ColorMath.Mix(c.Foreground, opposite, 0.1),
            ChromeDisabledHigh = ColorMath.Mix(c.Chrome, contrast, 0.1),
            ChromeDisabledLow = ColorMath.Mix(c.Foreground, c.Background, 0.45),
            ChromeGray = ColorMath.Mix(c.Foreground, c.Background, 0.4),
            ChromeWhite = theme.IsDark ? Colors.White : c.Background,
            ChromeBlackHigh = Colors.Black,
            ChromeBlackMedium = ColorMath.WithAlpha(Colors.Black, 0xCC),
            ChromeBlackMediumLow = ColorMath.WithAlpha(Colors.Black, 0x66),
            ChromeBlackLow = ColorMath.WithAlpha(Colors.Black, 0x33),
            ListLow = ColorMath.WithAlpha(contrast, 0x19),
            ListMedium = ColorMath.WithAlpha(contrast, 0x33),
            ErrorText = theme.IsDark ? Color.Parse("#FF6B6B") : Color.Parse("#C50500"),
        };
    }

    /// <summary>Brushes used by Dock and Inkitty's own styles. Replaced in place so DynamicResources update.</summary>
    private void FillBrushes(ThemeDefinition theme)
    {
        var c = theme.Colors;
        var contrast = theme.IsDark ? Colors.White : Colors.Black;
        var warm = theme.IsDark ? Color.Parse("#FFD54F") : Color.Parse("#F5B800");

        Dictionary<string, object> values = new()
        {
            // Dock reads RegionColor (a Color, not a brush) for its pane backgrounds.
            ["RegionColor"] = c.Sidebar,
            ["DockThemeBackgroundBrush"] = new SolidColorBrush(c.Sidebar),
            ["DockThemeControlBackgroundBrush"] = new SolidColorBrush(c.Background),
            ["DockThemeForegroundBrush"] = new SolidColorBrush(c.Foreground),
            ["DockThemeBorderLowBrush"] = new SolidColorBrush(ColorMath.Mix(c.Sidebar, contrast, 0.15)),
            ["DockThemeAccentBrush"] = new SolidColorBrush(c.Accent),
            ["DockSurfaceWorkbenchBrush"] = new SolidColorBrush(c.Sidebar),
            ["DockApplicationAccentBrushLow"] = new SolidColorBrush(c.Accent),
            ["DockApplicationAccentBrushMed"] = new SolidColorBrush(ColorMath.Mix(c.Accent, Colors.White, 0.15)),
            ["DockApplicationAccentBrushHigh"] = new SolidColorBrush(ColorMath.Mix(c.Accent, Colors.White, 0.3)),
            ["DockApplicationAccentBrushIndicator"] = new SolidColorBrush(c.Accent),
            ["DockApplicationAccentForegroundBrush"] = new SolidColorBrush(ColorMath.Luminance(c.Accent) > 0.6 ? Colors.Black : Colors.White),

            ["InkittyEditorBackground"] = new SolidColorBrush(c.Background),
            ["InkittyChromeBackground"] = new SolidColorBrush(c.Chrome),
            ["InkittyPlayerBackground"] = new SolidColorBrush(c.Background),
            ["InkittyPlayerForeground"] = new SolidColorBrush(c.Foreground),
            ["InkittyMuted"] = new SolidColorBrush(ColorMath.Mix(c.Foreground, c.Background, 0.4)),
            ["InkittyTagBackground"] = new SolidColorBrush(ColorMath.Mix(c.Background, contrast, 0.12)),
            ["InkittyChoiceBackground"] = new SolidColorBrush(ColorMath.Mix(c.Background, contrast, 0.07)),
            ["InkittyChoiceHover"] = new SolidColorBrush(ColorMath.Mix(c.Background, contrast, 0.14)),
            ["InkittyInfoBar"] = new SolidColorBrush(ColorMath.Mix(c.Background, warm, theme.IsDark ? 0.22 : 0.3)),
            ["InkittyNewHighlight"] = new SolidColorBrush(ColorMath.WithAlpha(warm, 0x33)),
        };

        foreach (var (key, value) in values)
        {
            _brushes[key] = value;
        }
    }
}
