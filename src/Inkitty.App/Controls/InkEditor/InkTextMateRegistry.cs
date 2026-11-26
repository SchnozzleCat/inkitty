using Avalonia.Platform;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>
/// TextMate registry serving the ink grammar (converted from inkle's Sublime syntax) plus one of
/// TextMateSharp's bundled VS Code themes.
/// </summary>
public sealed class InkTextMateRegistry : IRegistryOptions
{
    public const string InkScope = "source.ink";

    private static readonly Lazy<IRawGrammar> InkGrammar = new(LoadGrammar);
    private readonly RegistryOptions _themes;
    private readonly ThemeName _theme;

    public InkTextMateRegistry(ThemeName theme)
    {
        _theme = theme;
        _themes = new RegistryOptions(_theme);
    }

    public IRawTheme GetTheme(string scopeName) => _themes.GetTheme(scopeName);

    public IRawGrammar GetGrammar(string scopeName) =>
        scopeName == InkScope ? InkGrammar.Value : _themes.GetGrammar(scopeName);

    public ICollection<string> GetInjections(string scopeName) => _themes.GetInjections(scopeName);

    public IRawTheme GetDefaultTheme() => _themes.LoadTheme(_theme);

    private static IRawGrammar LoadGrammar()
    {
        using var stream = AssetLoader.Open(new Uri("avares://Inkitty.App/Assets/Grammars/ink.tmLanguage.json"));
        using var reader = new StreamReader(stream);
        return GrammarReader.ReadGrammarSync(reader);
    }
}
