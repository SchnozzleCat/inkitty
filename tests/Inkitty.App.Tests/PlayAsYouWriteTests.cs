using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.ViewModels.Tools;
using Inkitty.Core.Compilation;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class PlayAsYouWriteTests
{
    [Test]
    public async Task Opening_the_sample_compiles_plays_and_shows_the_editor() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));

            await Assert.That(app.Project.LastResult).IsTypeOf<CompileSucceeded>();
            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.Factory.PlayerTool.Choices.Count > 0)).IsTrue();
            var editor = app.EditorFor(app.Shell.ActiveDocument!);
            await Assert.That(editor).IsNotNull();
            await Assert.That(editor!.TextEditor.Document.Text).Contains("INCLUDE chapters/forest.ink");
        });

    [Test]
    public async Task Editing_text_keeps_the_players_position() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;

            // Go left into the forest, then light the torch.
            player.ChooseCommand.Execute(player.Choices.Single(c => c.Text.Contains("forest", StringComparison.Ordinal)));
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.Choices.Any(c => c.Text == "Light a torch"));
            player.ChooseCommand.Execute(player.Choices.Single(c => c.Text == "Light a torch"));
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 2);

            // Edit the forest file: the player should replay both choices and show the new text.
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);
            var compiled = await app.WaitForCompileAfter(() =>
                forest.Document.Text = forest.Document.Text.Replace("A quiet clearing.", "A sunny clearing.", StringComparison.Ordinal));

            await Assert.That(compiled).IsTrue();
            await Assert.That(await UiTestHelpers.WaitUntil(() => player.Items.OfType<PlayerParagraph>().Any(p => p.Text.Contains("sunny", StringComparison.Ordinal)))).IsTrue();
            await Assert.That(player.ChoiceCount).IsEqualTo(2);
            await Assert.That(player.Items.OfType<PlayerChoiceEcho>().Select(e => e.Text)).Contains("Light a torch");
        });

    [Test]
    public async Task Compile_error_keeps_last_good_run_and_marks_it_stale() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;
            var itemsBefore = player.Items.Count;

            var main = app.Shell.ActiveDocument!;
            await app.WaitForCompileAfter(() => main.Document.Insert(main.Document.TextLength, "\n-> nowhere_at_all\n"));

            await Assert.That(app.Project.LastResult).IsTypeOf<CompileFailed>();
            await Assert.That(player.IsStale).IsTrue();
            await Assert.That(player.Items.Count).IsEqualTo(itemsBefore);
            await Assert.That(app.Shell.ErrorCount).IsGreaterThanOrEqualTo(1);
        });

    [Test]
    public async Task Restart_and_step_back() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;

            player.ChooseCommand.Execute(player.Choices[0]);
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 1);

            player.StepBackCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 0);
            await Assert.That(player.Choices.Count).IsEqualTo(2); // "Light the torch" is hidden until has_torch

            player.ChooseCommand.Execute(player.Choices[0]);
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 1);
            player.RestartCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 0);
            await Assert.That(player.Items.OfType<PlayerChoiceEcho>()).IsEmpty();
        });
}
