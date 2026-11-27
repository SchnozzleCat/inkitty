using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Inkitty.App.ViewModels;
using Inkitty.Core.Settings;

namespace Inkitty.App.Input;

/// <summary>
/// How shortcuts are written on this platform: the modifier <c>Mod</c> stands for (Ctrl, or Cmd on
/// macOS) and whether the macOS defaults apply.
/// </summary>
public sealed record ShortcutPlatform(KeyModifiers CommandModifier, bool MacDefaults, bool IsBrowser)
{
    public static ShortcutPlatform Current => new(
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control,
        OperatingSystem.IsMacOS(),
        OperatingSystem.IsBrowser());

    public static readonly ShortcutPlatform Windows = new(KeyModifiers.Control, false, false);

    public static readonly ShortcutPlatform Mac = new(KeyModifiers.Meta, true, false);

    /// <summary>Gestures are shown in the macOS style (⌘⇧S) when Mod is Cmd.</summary>
    public bool UsesMacSymbols => CommandModifier == KeyModifiers.Meta;
}

/// <summary>A command that can have a keyboard shortcut.</summary>
/// <param name="Id">Stable id, the key in <see cref="AppSettings.KeyBindings"/>.</param>
/// <param name="DefaultGesture">Default shortcut (<c>Mod</c> = Ctrl/Cmd); empty for none.</param>
/// <param name="MacDefaultGesture">Default on macOS, if it differs.</param>
public sealed record ShortcutCommand(
    string Id,
    string Category,
    string Name,
    Func<MainWindowViewModel, ICommand> Command,
    string DefaultGesture = "",
    string? MacDefaultGesture = null)
{
    public string DefaultFor(ShortcutPlatform platform) =>
        platform.MacDefaults && MacDefaultGesture is not null ? MacDefaultGesture : DefaultGesture;
}

/// <summary>
/// Every command that can have a shortcut, with its default, and the helpers that turn the stored
/// gesture text (<see cref="AppSettings.KeyBindings"/>) into key gestures and back.
/// </summary>
public static class ShortcutCatalog
{
    /// <summary>Stands for the platform's command modifier in stored gestures.</summary>
    public const string ModToken = "Mod";

    public static IReadOnlyList<ShortcutCommand> Commands { get; } =
    [
        new("file.new", "File", "New project", vm => vm.NewProjectCommand, "Mod+N"),
        new("file.open", "File", "Open project", vm => vm.OpenCommand, "Mod+O"),
        new("file.save", "File", "Save", vm => vm.SaveCommand, "Mod+S"),
        new("file.saveAll", "File", "Save all", vm => vm.SaveAllCommand, "Mod+Shift+S"),
        new("file.export", "File", "Export project as .zip", vm => vm.ExportProjectCommand),
        new("file.closeTab", "File", "Close tab", vm => vm.CloseDocumentCommand, "Mod+W"),
        new("file.closeProject", "File", "Close project", vm => vm.CloseProjectCommandCommand),
        new("app.settings", "Edit", "Settings", vm => vm.OpenSettingsCommand, "Mod+OemComma"),
        new("app.keyboardShortcuts", "Edit", "Keyboard shortcuts", vm => vm.OpenKeyboardShortcutsCommand),
        new("go.definition", "Go", "Go to definition", vm => vm.GoToDefinitionCommand, "F12"),
        new("go.back", "Go", "Back", vm => vm.GoBackCommand, "Alt+Left", "Ctrl+OemMinus"),
        new("go.forward", "Go", "Forward", vm => vm.GoForwardCommand, "Alt+Right", "Ctrl+Shift+OemMinus"),
        new("go.nextIssue", "Go", "Next issue", vm => vm.NextIssueCommand, "F8"),
        new("go.previousIssue", "Go", "Previous issue", vm => vm.PreviousIssueCommand, "Shift+F8"),
        new("story.restart", "Story", "Restart story", vm => vm.RestartStoryCommand, "Mod+R"),
        new("story.stepBack", "Story", "Step back", vm => vm.StepBackStoryCommand),
        new("story.showCurrentLine", "Story", "Show current line", vm => vm.RevealStoryPositionCommand, "Mod+J"),
        new("story.map", "Story", "Show the map", vm => vm.ShowStoryMapCommand, "Mod+Shift+M"),
        new("view.files", "View", "Show files", vm => vm.ShowFilesCommand),
        new("view.outline", "View", "Show outline", vm => vm.ShowOutlineCommand),
        new("view.player", "View", "Show player", vm => vm.ShowPlayerCommand),
        new("view.issues", "View", "Show issues", vm => vm.ShowIssuesCommand),
        new("view.variables", "View", "Show variables", vm => vm.ShowVariablesCommand),
        new("view.resetLayout", "View", "Reset layout", vm => vm.ResetLayoutCommand),
    ];

    public static ShortcutCommand? Find(string id) => Commands.FirstOrDefault(c => c.Id == id);

    /// <summary>The shortcut for every command: the user's choice, or the default. Null means none.</summary>
    public static IReadOnlyDictionary<string, KeyGesture?> Resolve(AppSettings settings, ShortcutPlatform platform)
    {
        var result = new Dictionary<string, KeyGesture?>();
        foreach (var command in Commands)
        {
            // An unreadable stored gesture (hand-edited settings) falls back to the default.
            result[command.Id] = settings.KeyBindings.TryGetValue(command.Id, out var stored) && TryParse(stored, platform, out var gesture)
                ? gesture
                : Parse(command.DefaultFor(platform), platform);
        }

        return result;
    }

    /// <summary>
    /// Parses stored gesture text (<c>Mod+Shift+S</c>, <c>F12</c>); empty text is "no shortcut".
    /// </summary>
    public static bool TryParse(string? text, ShortcutPlatform platform, out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var command = platform.CommandModifier == KeyModifiers.Meta ? "Cmd" : "Ctrl";
        var normalized = string.Join('+', parts.Select(p => p.Equals(ModToken, StringComparison.OrdinalIgnoreCase) ? command : p));
        try
        {
            gesture = KeyGesture.Parse(normalized);
            return gesture.Key != Key.None;
        }
        catch (ArgumentException)
        {
            gesture = null;
            return false;
        }
    }

    private static KeyGesture? Parse(string text, ShortcutPlatform platform) =>
        TryParse(text, platform, out var gesture) ? gesture : throw new InvalidOperationException($"Bad default shortcut '{text}'");

    /// <summary>Gesture text for the settings file, with the command modifier written as <c>Mod</c>.</summary>
    public static string ToStored(KeyGesture? gesture, ShortcutPlatform platform)
    {
        if (gesture is null)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        var modifiers = gesture.KeyModifiers;
        if (modifiers.HasFlag(platform.CommandModifier))
        {
            parts.Add(ModToken);
            modifiers &= ~platform.CommandModifier;
        }

        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Cmd");
        }

        parts.Add(gesture.Key.ToString());
        return string.Join('+', parts);
    }

    /// <summary>
    /// Whether a gesture can be a shortcut. Shortcuts take the key before the focused control does, so a
    /// plain or Shift+ key would stop it being typed: they need Ctrl, Alt or Cmd, except F1–F24.
    /// </summary>
    public static bool IsAllowed(KeyGesture gesture) =>
        !IsModifierKey(gesture.Key)
        && (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)
            || gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)
            || gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)
            || gesture.Key is >= Key.F1 and <= Key.F24);

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None;

    /// <summary>Browsers keep these for themselves; the page never sees them.</summary>
    public static bool IsBrowserReserved(KeyGesture gesture, ShortcutPlatform platform)
    {
        var modifiers = gesture.KeyModifiers;
        if (!modifiers.HasFlag(platform.CommandModifier) || modifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        return gesture.Key is Key.N or Key.T or Key.W or Key.Tab or Key.Q;
    }

    /// <summary>The gesture as shown to the user: <c>Ctrl+Shift+S</c>, or <c>⇧⌘S</c> on macOS.</summary>
    public static string Format(KeyGesture? gesture, ShortcutPlatform platform)
    {
        if (gesture is null)
        {
            return string.Empty;
        }

        var key = KeyName(gesture.Key);
        var m = gesture.KeyModifiers;
        if (platform.UsesMacSymbols)
        {
            // macOS order: Control, Option, Shift, Command.
            return (m.HasFlag(KeyModifiers.Control) ? "⌃" : "")
                + (m.HasFlag(KeyModifiers.Alt) ? "⌥" : "")
                + (m.HasFlag(KeyModifiers.Shift) ? "⇧" : "")
                + (m.HasFlag(KeyModifiers.Meta) ? "⌘" : "")
                + key;
        }

        var parts = new List<string>();
        if (m.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (m.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (m.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (m.HasFlag(KeyModifiers.Meta))
        {
            parts.Add(OperatingSystem.IsWindows() ? "Win" : "Super");
        }

        parts.Add(key);
        return string.Join('+', parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
        >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(key - Key.NumPad0),
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe or Key.OemBackslash => "\\",
        Key.OemTilde => "`",
        Key.Left => "←",
        Key.Right => "→",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Enter => "Enter",
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.PageUp => "PageUp",
        Key.PageDown => "PageDown",
        Key.Add => "Num +",
        Key.Subtract => "Num -",
        Key.Multiply => "Num *",
        Key.Divide => "Num /",
        _ => key.ToString(),
    };
}
