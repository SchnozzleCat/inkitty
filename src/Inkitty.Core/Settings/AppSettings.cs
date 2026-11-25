namespace Inkitty.Core.Settings;

public sealed record WindowBounds(double X, double Y, double Width, double Height, bool IsMaximized);

/// <summary>User preferences, persisted as JSON in the per-OS config directory.</summary>
public sealed record AppSettings
{
    public const int MaxRecentProjects = 10;

    public AutoSaveMode AutoSave { get; init; } = AutoSaveMode.Off;

    public int AutoSaveDelayMs { get; init => field = Math.Clamp(value, 200, 60_000); } = 1000;

    public ThemePreference Theme { get; init; } = ThemePreference.System;

    /// <summary>Editor font; a comma-separated fallback list. The default picks each OS's standard monospace font.</summary>
    public string EditorFontFamily { get; init; } = DefaultEditorFont;

    public static string DefaultEditorFont =>
        OperatingSystem.IsWindows() ? "Cascadia Mono, Consolas, Courier New"
        : OperatingSystem.IsMacOS() ? "SF Mono, Menlo, Monaco"
        : "DejaVu Sans Mono, Liberation Mono, Noto Sans Mono, monospace";

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
