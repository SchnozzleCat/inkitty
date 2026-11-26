using Avalonia.Media;
using TextMateSharp.Grammars;

namespace Inkitty.App.Theming;

/// <summary>
/// The handful of colours a theme needs to style the whole window. Everything else
/// (Fluent palette, Dock panes, player) is derived from these.
/// </summary>
public sealed record ThemeColors(
    Color Background,
    Color Foreground,
    Color Sidebar,
    Color Chrome,
    Color Menu,
    Color Accent);

/// <summary>A colour theme: a TextMate theme for the editor plus matching window colours.</summary>
/// <param name="Id">Stable id stored in settings, e.g. <c>dracula</c>.</param>
public sealed record ThemeDefinition(string Id, string DisplayName, ThemeName TextMateTheme, bool IsDark, ThemeColors Colors)
{
    /// <summary>Foreground of a TextMate scope in this theme's syntax colours (e.g. <c>constant.numeric</c>), if it styles it.</summary>
    public Color? SyntaxColor(string scope) => ThemeCatalog.SyntaxColor(TextMateTheme, scope);

    public override string ToString() => DisplayName;
}
