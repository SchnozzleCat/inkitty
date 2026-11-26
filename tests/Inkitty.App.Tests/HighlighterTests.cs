using AvaloniaEdit;
using AvaloniaEdit.Document;
using Inkitty.App.Controls.InkEditor;
using Inkitty.App.Tests.Infrastructure;

namespace Inkitty.App.Tests;

/// <summary>The UI-thread TextMate highlighter (no tokenizer thread, so it works in single-threaded WebAssembly).</summary>
[NotInParallel("ui")]
public class HighlighterTests
{
    private const string Story = """
        VAR coins = 3
        === forest ===
        /* a block
        comment */
        You are in the forest. {coins > 2: Rich.}
        * [Go left] -> clearing
        = clearing
        -> END
        """;

    private static List<List<(int Start, string Scopes)>> AllTokens(InkHighlighter highlighter, TextDocument document) =>
        [.. Enumerable.Range(1, document.LineCount).Select(n => highlighter.TokensForLine(n).Select(t => (t.StartIndex, string.Join(' ', t.Scopes))).ToList())];

    private static (InkHighlighter Highlighter, TextDocument Document) Create(string text)
    {
        var document = new TextDocument(text);
        var editor = new TextEditor { Document = document };
        return (new InkHighlighter(editor, new InkTextMateRegistry(TextMateSharp.Grammars.ThemeName.LightPlus)), document);
    }

    [Test]
    public async Task Tokenizes_lines_with_state_carried_between_them() =>
        await HeadlessSession.Run(async () =>
        {
            var (highlighter, _) = Create(Story);

            await Assert.That(string.Join(' ', highlighter.TokensForLine(2).SelectMany(t => t.Scopes))).Contains("entity.name.function.knot.ink");
            await Assert.That(string.Join(' ', highlighter.TokensForLine(4).SelectMany(t => t.Scopes))).Contains("comment.block");
        });

    [Test]
    [Arguments(0, 0, "// new first line\n")]
    [Arguments(40, 0, "/* ")]
    [Arguments(40, 0, "\n\n")]
    [Arguments(14, 20, "")]
    [Arguments(30, 15, "x\ny\n*/\n")]
    public async Task Edits_match_a_fresh_tokenization(int offset, int length, string insert) =>
        await HeadlessSession.Run(async () =>
        {
            var (highlighter, document) = Create(Story);
            _ = AllTokens(highlighter, document); // warm the cache so the edit is incremental

            document.Replace(offset, Math.Min(length, document.TextLength - offset), insert);

            var (fresh, freshDocument) = Create(document.Text);
            await Assert.That(document.LineCount).IsEqualTo(freshDocument.LineCount);
            var incremental = AllTokens(highlighter, document);
            var expected = AllTokens(fresh, freshDocument);
            for (var i = 0; i < expected.Count; i++)
            {
                await Assert.That(incremental[i]).IsEquivalentTo(expected[i]);
            }
        });

    [Test]
    public async Task Follows_document_replacement() =>
        await HeadlessSession.Run(async () =>
        {
            var document = new TextDocument("Plain");
            var editor = new TextEditor { Document = document };
            using var highlighter = new InkHighlighter(editor, new InkTextMateRegistry(TextMateSharp.Grammars.ThemeName.LightPlus));

            editor.Document = new TextDocument("=== knot ===");

            await Assert.That(string.Join(' ', highlighter.TokensForLine(1).SelectMany(t => t.Scopes))).Contains("entity.name.function.knot.ink");
        });

    [Test]
    public async Task Exposes_theme_gui_colours_and_switches_theme() =>
        await HeadlessSession.Run(async () =>
        {
            var (highlighter, _) = Create(Story);
            highlighter.TryGetThemeColor("editor.background", out var light);

            highlighter.SetTheme(new InkTextMateRegistry(TextMateSharp.Grammars.ThemeName.DarkPlus));
            highlighter.TryGetThemeColor("editor.background", out var dark);

            await Assert.That(dark).IsNotNull();
            await Assert.That(dark).IsNotEqualTo(light);
        });
}
