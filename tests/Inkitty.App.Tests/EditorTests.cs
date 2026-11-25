using Inkitty.App.Controls.InkEditor;
using Inkitty.App.Tests.Infrastructure;
using TextMateSharp.Registry;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class EditorTests
{
    private static List<string> ScopesOf(string line)
    {
        var registry = new Registry(new InkTextMateRegistry(dark: false));
        var grammar = registry.LoadGrammar(InkTextMateRegistry.InkScope);
        var result = grammar.TokenizeLine(line);
        return [.. result.Tokens.SelectMany(t => t.Scopes)];
    }

    [Test]
    [Arguments("=== forest ===", "entity.name.knot")]
    [Arguments("= clearing", "entity.name.stitch")]
    [Arguments("-> forest.clearing", "keyword.operator.divert")]
    [Arguments("INCLUDE chapters/forest.ink", "keyword.control.include")]
    [Arguments("// a comment", "comment.line.double-slash.js")]
    [Arguments("TODO: fix", "comment.todo")]
    public async Task Ink_grammar_tokenizes_core_constructs(string line, string expectedScope) =>
        await HeadlessSession.Run(async () =>
        {
            var scopes = ScopesOf(line);
            await Assert.That(scopes).Contains(expectedScope);
        });

    [Test]
    public async Task Word_wrap_setting_applies_to_editor() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var editor = app.EditorFor(app.Shell.ActiveDocument!)!;
            await Assert.That(editor.TextEditor.WordWrap).IsTrue();

            editor.Settings = app.Settings.Current with { WordWrap = false, EditorFontSize = 20 };
            await Assert.That(editor.TextEditor.WordWrap).IsFalse();
            await Assert.That(editor.TextEditor.FontSize).IsEqualTo(20);
        });
}
