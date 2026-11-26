using Avalonia.Media;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace Inkitty.App.Theming;

/// <summary>
/// The built-in themes: TextMateSharp's bundled VS Code themes, with window colours read from each
/// theme's own UI colours (sidebar, title bar, menus, focus border).
/// </summary>
public static class ThemeCatalog
{
    /// <summary>Follows the OS: Light+ or Dark+.</summary>
    public const string SystemId = "system";

    public const string LightPlusId = "light-plus";
    public const string DarkPlusId = "dark-plus";

    // Hand-tuned to match the window chrome Inkitty shipped with.
    private static readonly ThemeColors LightPlusColors = new(
        Background: Color.Parse("#FFFFFF"),
        Foreground: Color.Parse("#1F1F1F"),
        Sidebar: Color.Parse("#F3F3F3"),
        Chrome: Color.Parse("#E8E8E8"),
        Menu: Color.Parse("#F8F8F8"),
        Accent: Color.Parse("#0078D4"));

    private static readonly ThemeColors DarkPlusColors = new(
        Background: Color.Parse("#1E1E1E"),
        Foreground: Color.Parse("#E6E6E6"),
        Sidebar: Color.Parse("#252526"),
        Chrome: Color.Parse("#2D2D30"),
        Menu: Color.Parse("#2D2D30"),
        Accent: Color.Parse("#0078D4"));

    // (id, name, TextMate theme) in the order shown in the UI. Duplicates and bare VS themes are skipped.
    private static readonly (string Id, string Name, ThemeName Theme)[] Sources =
    [
        (LightPlusId, "Light+", ThemeName.LightPlus),
        (DarkPlusId, "Dark+", ThemeName.DarkPlus),
        ("visual-studio-light", "Visual Studio Light", ThemeName.VisualStudioLight),
        ("visual-studio-dark", "Visual Studio Dark", ThemeName.VisualStudioDark),
        ("one-light", "One Light", ThemeName.AtomOneLight),
        ("one-dark", "One Dark", ThemeName.AtomOneDark),
        ("dracula", "Dracula", ThemeName.Dracula),
        ("monokai", "Monokai", ThemeName.Monokai),
        ("dimmed-monokai", "Dimmed Monokai", ThemeName.DimmedMonokai),
        ("solarized-light", "Solarized Light", ThemeName.SolarizedLight),
        ("solarized-dark", "Solarized Dark", ThemeName.SolarizedDark),
        ("quiet-light", "Quiet Light", ThemeName.QuietLight),
        ("kimbie-dark", "Kimbie Dark", ThemeName.KimbieDark),
        ("abyss", "Abyss", ThemeName.Abbys),
        ("tomorrow-night-blue", "Tomorrow Night Blue", ThemeName.TomorrowNightBlue),
        ("red", "Red", ThemeName.Red),
        ("high-contrast-light", "High Contrast Light", ThemeName.HighContrastLight),
        ("high-contrast-dark", "High Contrast Dark", ThemeName.HighContrastDark),
    ];

    private static readonly Lazy<IReadOnlyList<ThemeDefinition>> LazyThemes = new(() => [.. Sources.Select(Build)]);

    /// <summary>All concrete themes (excluding the "System" choice).</summary>
    public static IReadOnlyList<ThemeDefinition> Themes => LazyThemes.Value;

    public static ThemeDefinition LightPlus => Get(LightPlusId);

    public static ThemeDefinition DarkPlus => Get(DarkPlusId);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(ThemeName, string), Color?> SyntaxColors = [];

    /// <summary>The colour a TextMate theme gives a scope (e.g. <c>string.quoted</c>), or null if it leaves it unstyled.</summary>
    public static Color? SyntaxColor(ThemeName name, string scope) => SyntaxColors.GetOrAdd((name, scope), key =>
    {
        var registry = new RegistryOptions(key.Item1);
        var theme = Theme.CreateFromRawTheme(registry.LoadTheme(key.Item1), registry);
        var rule = theme.Match([key.Item2]).FirstOrDefault();
        return rule is { foreground: > 0 } ? ColorMath.ParseVsCode(theme.GetColor(rule.foreground)) : null;
    });

    /// <summary>The theme with this id, or null if unknown.</summary>
    public static ThemeDefinition? Find(string? id) => Themes.FirstOrDefault(t => t.Id == id);

    private static ThemeDefinition Get(string id) => Find(id) ?? throw new InvalidOperationException($"Missing built-in theme {id}");

    private static ThemeDefinition Build((string Id, string Name, ThemeName Theme) source)
    {
        if (source.Id == LightPlusId)
        {
            return new ThemeDefinition(source.Id, source.Name, source.Theme, IsDark: false, LightPlusColors);
        }

        if (source.Id == DarkPlusId)
        {
            return new ThemeDefinition(source.Id, source.Name, source.Theme, IsDark: true, DarkPlusColors);
        }

        var ui = ReadUiColors(source.Theme);
        Color? Get(params string[] keys) =>
            keys.Select(k => ColorMath.ParseVsCode(ui.GetValueOrDefault(k))).FirstOrDefault(c => c is not null);

        var background = Get("editor.background") ?? Color.Parse("#FFFFFF");
        var isDark = ColorMath.Luminance(background) < 0.5;
        var foreground = ColorMath.Over(Get("editor.foreground", "foreground") ?? (isDark ? Color.Parse("#D4D4D4") : Color.Parse("#333333")), background);

        // A slightly darker (light themes) or lighter (dark themes) step from the editor for panels.
        Color Step(double amount) => ColorMath.Mix(background, isDark ? Colors.White : Colors.Black, amount);

        var sidebar = ColorMath.Over(Get("sideBar.background") ?? Step(0.04), background);
        var chrome = ColorMath.Over(Get("titleBar.activeBackground", "editorGroupHeader.tabsBackground") ?? Step(0.08), background);
        var menu = ColorMath.Over(Get("menu.background", "editorWidget.background", "dropdown.background") ?? Step(0.06), background);
        var accent = ColorMath.Over(Get("focusBorder", "button.background", "activityBarBadge.background", "textLink.foreground") ?? Color.Parse("#0078D4"), background);

        return new ThemeDefinition(source.Id, source.Name, source.Theme, isDark, new ThemeColors(background, foreground, sidebar, chrome, menu, accent));
    }

    /// <summary>The theme's VS Code UI colours, following <c>include</c> chains (e.g. Dark+ → Dark (Visual Studio)).</summary>
    private static Dictionary<string, string> ReadUiColors(ThemeName name)
    {
        var registry = new RegistryOptions(name);
        Dictionary<string, string> result = [];
        IRawTheme? theme = registry.LoadTheme(name);
        for (var depth = 0; theme is not null && depth < 4; depth++)
        {
            foreach (var (key, value) in theme.GetGuiColors() ?? [])
            {
                if (value is string s)
                {
                    result.TryAdd(key, s);
                }
            }

            theme = theme.GetInclude() is { Length: > 0 } include ? registry.GetTheme(include) : null;
        }

        return result;
    }
}
