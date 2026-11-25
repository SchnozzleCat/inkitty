namespace Inkitty.Core.Settings;

/// <summary>Per-OS locations for Inkitty's configuration files.</summary>
public static class AppPaths
{
    public const string AppFolderName = "Inkitty";

    /// <summary>
    /// Linux: <c>$XDG_CONFIG_HOME/Inkitty</c> (or <c>~/.config/Inkitty</c>);
    /// Windows: <c>%APPDATA%\Inkitty</c>; macOS: <c>~/Library/Application Support/Inkitty</c>.
    /// </summary>
    public static string ConfigDirectory(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        if (getEnvironmentVariable("INKITTY_CONFIG_DIR") is { Length: > 0 } overridden)
        {
            return overridden;
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", AppFolderName);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName);
        }

        var xdg = getEnvironmentVariable("XDG_CONFIG_HOME");
        var baseDir = string.IsNullOrEmpty(xdg)
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdg;
        return Path.Join(baseDir, AppFolderName);
    }
}
