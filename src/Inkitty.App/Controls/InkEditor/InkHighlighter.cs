using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using TmFontStyle = TextMateSharp.Themes.FontStyle;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>
/// TextMate syntax highlighting without a background thread. AvaloniaEdit.TextMate tokenizes on a
/// worker that blocks on wait handles, which hangs single-threaded WebAssembly. This colorizer
/// tokenizes lazily on the UI thread instead: lines are tokenized in order as they are drawn, and
/// each line's end state is cached so edits only re-tokenize from the changed line down.
/// </summary>
public sealed class InkHighlighter : DocumentColorizingTransformer, IDisposable
{
    /// <summary>Per-line time limit for the tokenizer (pathological lines are left uncoloured).</summary>
    private static readonly TimeSpan LineTimeLimit = TimeSpan.FromMilliseconds(500);

    /// <summary>Lines longer than this are not highlighted (as in AvaloniaEdit).</summary>
    private const int MaxLineLength = 3000;

    private readonly TextEditor _editor;
    private readonly IGrammar _grammar;
    private readonly List<LineState> _lines = [];
    private Theme _theme;
    private Dictionary<int, IBrush> _brushes = [];
    private TextDocument? _document;

    /// <summary>Lines before this index have up-to-date tokens.</summary>
    private int _validUpTo;

    public InkHighlighter(TextEditor editor, InkTextMateRegistry registryOptions)
    {
        _editor = editor;
        var registry = new Registry(registryOptions);
        _grammar = registry.LoadGrammar(InkTextMateRegistry.InkScope)
            ?? throw new InvalidOperationException("The ink grammar could not be loaded.");
        _theme = registry.GetTheme();
        ApplyTheme(_theme);
        SetDocument(editor.Document);
        editor.DocumentChanged += OnDocumentChanged;
        editor.TextArea.TextView.LineTransformers.Add(this);
    }

    /// <summary>Colours from the theme's "colors" section (editor.background, ...).</summary>
    public ReadOnlyDictionary<string, string> GuiColors { get; private set; } = new(new Dictionary<string, string>());

    public bool TryGetThemeColor(string key, out string? value) => GuiColors.TryGetValue(key, out value);

    /// <summary>Switches theme (keeps cached tokens; only colours change).</summary>
    public void SetTheme(InkTextMateRegistry registryOptions)
    {
        var registry = new Registry(registryOptions);
        _theme = registry.GetTheme();
        ApplyTheme(_theme);
        _editor.TextArea.TextView.Redraw();
    }

    /// <summary>Tokens for a line (1-based), tokenizing any earlier lines first. Exposed for tests.</summary>
    internal IReadOnlyList<IToken> TokensForLine(int lineNumber) => EnsureTokenized(lineNumber - 1).Tokens ?? [];

    public void Dispose()
    {
        _editor.DocumentChanged -= OnDocumentChanged;
        _editor.TextArea.TextView.LineTransformers.Remove(this);
        SetDocument(null);
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length > MaxLineLength || _document is null)
        {
            return;
        }

        var tokens = EnsureTokenized(line.LineNumber - 1).Tokens ?? [];
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var start = Math.Min(token.StartIndex, line.Length);
            var end = Math.Min(i + 1 < tokens.Count ? tokens[i + 1].StartIndex : line.Length, line.Length);
            if (end <= start || token.Scopes is not { Count: > 0 } scopes)
            {
                continue;
            }

            var (foreground, background, fontStyle) = Style(scopes);
            if (foreground is null && background is null && fontStyle <= 0)
            {
                continue;
            }

            ChangeLinePart(line.Offset + start, line.Offset + end, element =>
            {
                if (foreground is not null)
                {
                    element.TextRunProperties.SetForegroundBrush(foreground);
                }

                if (background is not null)
                {
                    element.BackgroundBrush = background;
                }

                if (fontStyle > 0)
                {
                    var typeface = element.TextRunProperties.Typeface;
                    element.TextRunProperties.SetTypeface(new Typeface(
                        typeface.FontFamily,
                        (fontStyle & TmFontStyle.Italic) != 0 ? Avalonia.Media.FontStyle.Italic : typeface.Style,
                        (fontStyle & TmFontStyle.Bold) != 0 ? FontWeight.Bold : typeface.Weight,
                        typeface.Stretch));
                    if ((fontStyle & TmFontStyle.Underline) != 0)
                    {
                        element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                    }
                }
            });
        }
    }

    private (IBrush? Foreground, IBrush? Background, TmFontStyle FontStyle) Style(IList<string> scopes)
    {
        int foreground = 0, background = 0;
        var fontStyle = TmFontStyle.NotSet;
        foreach (var rule in _theme.Match(scopes))
        {
            if (foreground == 0 && rule.foreground > 0)
            {
                foreground = rule.foreground;
            }

            if (background == 0 && rule.background > 0)
            {
                background = rule.background;
            }

            if (fontStyle == TmFontStyle.NotSet && rule.fontStyle > 0)
            {
                fontStyle = rule.fontStyle;
            }
        }

        return (_brushes.GetValueOrDefault(foreground), _brushes.GetValueOrDefault(background), fontStyle);
    }

    /// <summary>Tokenizes lines up to <paramref name="index"/> (0-based) as needed and returns its state.</summary>
    private LineState EnsureTokenized(int index)
    {
        var document = _document!;
        while (_validUpTo <= index)
        {
            var i = _validUpTo;
            var previous = i == 0 ? null : _lines[i - 1].EndState;
            var line = document.GetLineByNumber(i + 1);
            LineState state;
            if (line.Length > MaxLineLength)
            {
                state = new LineState([], previous, Valid: true);
            }
            else
            {
                var result = _grammar.TokenizeLine(document.GetText(line.Offset, line.Length), previous, LineTimeLimit);
                state = new LineState(result.Tokens, result.RuleStack, Valid: true);
            }

            // If this line ends in the same state as before the edit, the stale lines after it would
            // tokenize exactly as before: revalidate them without re-tokenizing.
            var endsAsBefore = _lines[i].EndState is { } old && Equals(old, state.EndState);
            _lines[i] = state;
            _validUpTo++;
            if (endsAsBefore)
            {
                while (_validUpTo < _lines.Count && _lines[_validUpTo] is { Valid: false, Tokens: not null } stale)
                {
                    _lines[_validUpTo] = stale with { Valid = true };
                    _validUpTo++;
                }
            }
        }

        return _lines[index];
    }

    private void ApplyTheme(Theme theme)
    {
        Dictionary<int, IBrush> brushes = [];
        foreach (var color in theme.GetColorMap())
        {
            if (Color.TryParse(Normalize(color), out var parsed))
            {
                brushes[theme.GetColorId(color)] = new ImmutableSolidColorBrush(parsed);
            }
        }

        _brushes = brushes;
        GuiColors = theme.GetGuiColorDictionary();
    }

    /// <summary>TextMate colours are #RGB, #RGBA, #RRGGBB or #RRGGBBAA; Avalonia wants alpha first.</summary>
    private static string Normalize(string color)
    {
        var hex = color.TrimStart('#');
        return hex.Length switch
        {
            3 => $"#{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}",
            4 => $"#{hex[3]}{hex[3]}{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}",
            8 => $"#{hex[6..8]}{hex[..6]}",
            _ => color,
        };
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e) => SetDocument(e.NewDocument);

    private void SetDocument(TextDocument? document)
    {
        if (_document is not null)
        {
            _document.Changed -= OnTextChanged;
        }

        _document = document;
        _lines.Clear();
        _validUpTo = 0;
        if (document is not null)
        {
            document.Changed += OnTextChanged;
            for (var i = 0; i < document.LineCount; i++)
            {
                _lines.Add(LineState.Invalid);
            }
        }
    }

    /// <summary>
    /// Keeps one entry per line. Touched lines lose their tokens; every later line is marked stale but
    /// keeps its tokens and end state, so re-tokenizing can stop as soon as a line ends as it did before.
    /// </summary>
    private void OnTextChanged(object? sender, DocumentChangeEventArgs e)
    {
        var document = _document!;
        var first = document.GetLineByOffset(Math.Min(e.Offset, document.TextLength)).LineNumber - 1;
        var removedLines = Math.Min(CountNewlines(e.RemovedText.Text), Math.Max(0, _lines.Count - first - 1));
        var insertedLines = CountNewlines(e.InsertedText.Text);

        // Lines first..first+removed merge into one, which now ends where the last of them ended:
        // keep that entry. Inserting splits line first; the last piece ends where it used to.
        _lines.RemoveRange(first, removedLines);
        for (var i = 0; i < insertedLines; i++)
        {
            _lines.Insert(first, LineState.Invalid);
        }

        // Safety net: the bookkeeping must match the document exactly.
        while (_lines.Count < document.LineCount)
        {
            _lines.Add(LineState.Invalid);
        }

        if (_lines.Count > document.LineCount)
        {
            _lines.RemoveRange(document.LineCount, _lines.Count - document.LineCount);
        }

        for (var i = first; i < _lines.Count; i++)
        {
            _lines[i] = i <= first + insertedLines
                ? _lines[i] with { Valid = false, Tokens = null }
                : _lines[i] with { Valid = false };
        }

        _validUpTo = Math.Min(_validUpTo, first);
    }

    private static int CountNewlines(string text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
            {
                count++;
            }
        }

        return count;
    }

    /// <param name="Tokens">The line's tokens; null once its text changed (it must be re-tokenized).</param>
    /// <param name="EndState">The tokenizer state at the end of the line.</param>
    /// <param name="Valid">False if the line may need re-tokenizing (an earlier line changed).</param>
    private sealed record LineState(IReadOnlyList<IToken>? Tokens, IStateStack? EndState, bool Valid)
    {
        public static LineState Invalid { get; } = new(null, null, Valid: false);
    }
}
