namespace Inkitty.Core.Settings;

public sealed record WindowBounds(double X, double Y, double Width, double Height, bool IsMaximized);

/// <summary>User preferences, persisted as JSON in the per-OS config directory.</summary>
public sealed record AppSettings
{
    public const int MaxRecentProjects = 10;

    public AutoSaveMode AutoSave { get; init; } = AutoSaveMode.Off;

    public int AutoSaveDelayMs { get; init => field = Math.Clamp(value, 200, 60_000); } = 1000;

    /// <summary>Colour theme id (see the app's theme catalog), or <c>system</c> to follow the OS light/dark setting.</summary>
    public string ColorTheme { get; init; } = "system";

    /// <summary>Editor font; a comma-separated fallback list. The default picks each OS's standard monospace font.</summary>
    public string EditorFontFamily { get; init; } = DefaultEditorFont;

    /// <summary>
    /// JetBrains Mono is bundled with the app (so it looks the same everywhere); the OS monospace
    /// fonts after it are only fallbacks.
    /// </summary>
    public const string DefaultEditorFont = BundledMonoFont + ", Cascadia Mono, Menlo, Consolas, DejaVu Sans Mono, monospace";

    /// <summary>Earlier default values, upgraded to <see cref="DefaultEditorFont"/> on load.</summary>
    internal static readonly string[] LegacyDefaultEditorFonts =
    [
        "Cascadia Mono, Menlo, Consolas, DejaVu Sans Mono, monospace",
        "Cascadia Mono, Consolas, Courier New",
        "SF Mono, Menlo, Monaco",
        "DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace",
    ];

    /// <summary>Avalonia name of the bundled JetBrains Mono (a bare "JetBrains Mono" only finds an installed copy).</summary>
    public const string BundledMonoFont = "fonts:Inkitty#JetBrains Mono";

    public double EditorFontSize { get; init => field = Math.Clamp(value, 6, 72); } = 14;

    public bool WordWrap { get; init; } = true;

    public int CompileDebounceMs { get; init => field = Math.Clamp(value, 50, 5000); } = 450;

    public int ReplayTimeBudgetMs { get; init => field = Math.Clamp(value, 100, 60_000); } = 2000;

    /// <summary>
    /// Alt/Option+click goes to definition (Inky parity). Off by default on Linux, where many
    /// window managers use Alt+drag to move windows. Ctrl/Cmd+click always works.
    /// </summary>
    public bool AltClickNavigates { get; init; } = !OperatingSystem.IsLinux();

    public IReadOnlyList<string> RecentProjects { get; init; } = [];

    public WindowBounds? LastWindowBounds { get; init; }

    public AppSettings WithRecentProject(string mainFilePath) => this with
    {
        RecentProjects =
        [
            mainFilePath,
            .. RecentProjects.Where(p => !FileSystem.PathComparer.PathEquals(p, mainFilePath)).Take(MaxRecentProjects - 1),
        ],
    };
}
