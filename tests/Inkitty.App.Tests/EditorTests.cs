using Inkitty.App.Controls.InkEditor;
using Inkitty.App.Tests.Infrastructure;
using TextMateSharp.Registry;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class EditorTests
{
    private static List<List<(string Text, IReadOnlyList<string> Scopes)>> TokenizeLines(params string[] lines)
    {
        var registry = new Registry(new InkTextMateRegistry(TextMateSharp.Grammars.ThemeName.LightPlus));
        var grammar = registry.LoadGrammar(InkTextMateRegistry.InkScope);
        TextMateSharp.Grammars.IStateStack? state = null;
        List<List<(string, IReadOnlyList<string>)>> result = [];
        foreach (var line in lines)
        {
            var tokens = grammar.TokenizeLine(line, state, TimeSpan.MaxValue);
            state = tokens.RuleStack;
            result.Add([.. tokens.Tokens.Select(t => (line[Math.Min(t.StartIndex, line.Length)..Math.Min(t.EndIndex, line.Length)], (IReadOnlyList<string>)t.Scopes))]);
        }

        return result;
    }

    /// <summary>The scopes of the token whose text is <paramref name="token"/> (trimmed), or else the first token containing it.</summary>
    private static IReadOnlyList<string> ScopesOf(string line, string token)
    {
        var tokens = TokenizeLines(line)[0];
        var match = tokens.FirstOrDefault(t => t.Text.Trim() == token);
        if (match.Scopes is null)
        {
            match = tokens.FirstOrDefault(t => token.Contains(t.Text.Trim(), StringComparison.Ordinal) && t.Text.Trim().Length > 0);
        }

        return match.Scopes ?? [];
    }

    [Test]
    [Arguments("=== forest ===", "forest", "entity.name.function.knot.ink")]
    [Arguments("=== function courage(ref x) ===", "function", "storage.type.function.ink")]
    [Arguments("=== function courage(ref x) ===", "x", "variable.parameter.ink")]
    [Arguments("= clearing", "clearing", "entity.name.function.stitch.ink")]
    [Arguments("-> forest.clearing", "->", "keyword.control.flow.divert.ink")]
    [Arguments("-> forest.clearing", "forest.clearing", "entity.name.function.divert-target.ink")]
    [Arguments("-> END", "END", "constant.language.flow.ink")]
    [Arguments("->->", "->->", "keyword.control.flow.tunnel-return.ink")]
    [Arguments("INCLUDE chapters/forest.ink", "INCLUDE", "keyword.control.import.ink")]
    [Arguments("// a comment", "// a comment", "comment.line.double-slash.ink")]
    [Arguments("TODO: fix", "TODO", "keyword.other.todo.ink")]
    [Arguments("VAR coins = 3", "coins", "variable.other.declaration.ink")]
    [Arguments("VAR coins = 3", "3", "constant.numeric.ink")]
    [Arguments("LIST mood = calm, (nervous)", "nervous", "variable.other.enummember.ink")]
    [Arguments("~ temp total = coins * 2", "temp", "storage.type.temp.ink")]
    [Arguments("~ temp total = coins * 2", "coins", "variable.other.ink")]
    [Arguments("~ playSound(\"door\")", "playSound", "entity.name.function.call.ink")]
    [Arguments("~ playSound(\"door\")", "door", "string.quoted.double.ink")]
    [Arguments("* (left) {not lit} [Go left] -> forest", "*", "keyword.control.choice.ink")]
    [Arguments("* (left) {not lit} [Go left] -> forest", "left", "entity.name.function.label.ink")]
    [Arguments("* (left) {not lit} [Go left] -> forest", "not", "keyword.operator.word.ink")]
    [Arguments("* (left) {not lit} [Go left] -> forest", "Go left", "markup.italic.choice-only.ink")]
    [Arguments("- (regroup) Fine.", "-", "keyword.control.gather.ink")]
    [Arguments("It was {lit: bright|dark}.", "lit", "variable.other.ink")]
    [Arguments("It was {lit: bright|dark}.", "|", "keyword.operator.branch.ink")]
    [Arguments("{&Rain|Wind}", "&", "keyword.control.sequence.ink")]
    [Arguments("Hello # mood: calm", "mood: calm", "entity.name.tag.ink")]
    [Arguments("Text<>", "<>", "keyword.operator.glue.ink")]
    [Arguments("{ stopping:", "stopping", "keyword.control.sequence.ink")]
    [Arguments("VAR café = 1", "café", "variable.other.declaration.ink")]
    public async Task Ink_grammar_tokenizes_core_constructs(string line, string token, string expectedScope) =>
        await HeadlessSession.Run(async () =>
        {
            await Assert.That(ScopesOf(line, token)).Contains(expectedScope);
        });

    [Test]
    public async Task Plain_story_text_is_left_unstyled() =>
        await HeadlessSession.Run(async () =>
        {
            await Assert.That(ScopesOf("Once upon a time, you stood at a crossroads.", "Once upon a time, you stood at a crossroads.")).IsEquivalentTo(["source.ink"]);
        });

    [Test]
    public async Task Multi_line_conditionals_highlight_their_clauses() =>
        await HeadlessSession.Run(async () =>
        {
            var lines = TokenizeLines("{ coins > 3:", "    - You are rich.", "- else:", "    You are poor.", "}");

            await Assert.That(lines[0].Single(t => t.Text == "coins").Scopes).Contains("variable.other.ink");
            await Assert.That(lines[2].Single(t => t.Text == "else").Scopes).Contains("keyword.control.conditional.else.ink");
            await Assert.That(lines[3].Single(t => t.Text.Trim() == "You are poor.").Scopes).Contains("meta.block.conditional.ink");
            await Assert.That(lines[4].Single(t => t.Text == "}").Scopes).Contains("punctuation.section.block.end.ink");
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

    [Test]
    public async Task Repeated_navigation_flashes_the_line_once_instead_of_stacking() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var editor = app.EditorFor(app.Shell.ActiveDocument!)!;
            var renderers = editor.TextEditor.TextArea.TextView.BackgroundRenderers;
            var before = renderers.Count;

            for (var i = 0; i < 5; i++)
            {
                app.Shell.Factory.StoryMapTool.OpenNodeCommand.Execute("start");
                await UiTestHelpers.WaitUntil(() => editor.IsFlashing);
            }

            await Assert.That(renderers.Count).IsEqualTo(before + 1);
            await Assert.That(await UiTestHelpers.WaitUntil(() => !editor.IsFlashing, 3000)).IsTrue();
            await Assert.That(renderers.Count).IsEqualTo(before);
        });

    [Test]
    public async Task No_separator_line_between_line_numbers_and_text() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var editor = app.EditorFor(app.Shell.ActiveDocument!)!;
            var margins = editor.TextEditor.TextArea.LeftMargins;

            await Assert.That(editor.TextEditor.ShowLineNumbers).IsTrue();
            await Assert.That(margins.Any(m => m is AvaloniaEdit.Editing.LineNumberMargin && m.IsVisible)).IsTrue();
            await Assert.That(margins.Where(AvaloniaEdit.Editing.DottedLineMargin.IsDottedLineMargin).All(m => !m.IsVisible)).IsTrue();
        });
}
