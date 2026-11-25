using Inkitty.App.Tests.Infrastructure;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Symbols;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class NavigationTests
{
    [Test]
    public async Task Go_to_definition_opens_the_other_file_at_the_stitch() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            File.AppendAllText(main, "\n=== shortcut ===\n-> forest.clearing\n");
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);

            var mainDoc = app.Shell.ActiveDocument!;
            var lines = mainDoc.Document.Text.Split('\n');
            var line = Array.FindIndex(lines, l => l.Contains("-> forest.clearing", StringComparison.Ordinal)) + 1;
            var column = lines[line - 1].IndexOf("clearing", StringComparison.Ordinal) + 2;

            await Assert.That(mainDoc.GoToDefinitionAt(new SourcePosition(line, column))).IsTrue();
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.RelativePath == "chapters/forest.ink" && app.Shell.ActiveDocument.Caret.Line > 1);

            await Assert.That(app.Shell.ActiveDocument!.RelativePath).IsEqualTo("chapters/forest.ink");
            await Assert.That(app.Shell.ActiveDocument.Caret.Line).IsEqualTo(9); // "= clearing"
            await Assert.That(app.Shell.GoBackCommand.CanExecute(null)).IsTrue();

            app.Shell.GoBackCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.RelativePath == "main.ink");
            await Assert.That(app.Shell.ActiveDocument!.RelativePath).IsEqualTo("main.ink");
        });

    [Test]
    public async Task Go_to_definition_works_while_index_is_stale() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            app.Project.RequestCompile(); // no-op; then type without waiting for the compile

            var main = app.Shell.ActiveDocument!;
            main.Document.Insert(0, "-> town\n");
            await Assert.That(app.Project.CurrentSymbols).IsNull();

            await Assert.That(main.GoToDefinitionAt(new SourcePosition(1, 5))).IsTrue();
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.RelativePath == "chapters/town.ink");
            await Assert.That(app.Shell.ActiveDocument!.RelativePath).IsEqualTo("chapters/town.ink");
        });

    [Test]
    public async Task Clicking_an_issue_navigates_and_markers_are_placed() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));

            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);
            await app.WaitForCompileAfter(() =>
                forest.Document.Text = forest.Document.Text.Replace("-> town.square", "-> town.sqare", StringComparison.Ordinal));

            var issues = app.Shell.Factory.IssuesTool;
            var error = issues.Items.FirstOrDefault(i => i.Severity == IssueSeverity.Error);
            await Assert.That(error).IsNotNull();
            await Assert.That(error!.File).IsEqualTo("chapters/forest.ink");
            await Assert.That(forest.Issues.Any(i => i.Severity == IssueSeverity.Error)).IsTrue();

            app.Shell.OpenDocument(app.Project.Project.Main);
            issues.OpenCommand.Execute(error);
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument == forest && forest.Caret.Line == error.Issue.Line);
            await Assert.That(app.Shell.ActiveDocument).IsEqualTo(forest);
            await Assert.That(forest.Caret.Line).IsEqualTo(error.Issue.Line!.Value);

            var editor = app.EditorFor(forest);
            await Assert.That(editor!.Markers.Markers.Count).IsGreaterThan(0);
        });

    [Test]
    public async Task Todos_are_listed_and_f8_cycles() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));

            await Assert.That(app.Shell.TodoCount).IsEqualTo(1);
            app.Shell.NextIssueCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.Caret.Line == 12);
            await Assert.That(app.Shell.ActiveDocument!.Caret.Line).IsEqualTo(12);
        });
}
