using Inkitty.App.Docking;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class VariablesTests
{
    private static async Task Choose(PlayerToolViewModel player, string text)
    {
        player.ChooseCommand.Execute(player.Choices.Single(c => c.Text.Contains(text, StringComparison.Ordinal)));
        await UiTestHelpers.WaitUntil(() => !player.IsBusy);
    }

    [Test]
    public async Task Variables_pane_is_tabbed_with_issues_by_default() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;

            await Assert.That(factory.VariablesTool.Owner?.Id).IsEqualTo(DockIds.BottomDock);
            await Assert.That(factory.VariablesTool.Owner).IsEqualTo(factory.IssuesTool.Owner);
            await Assert.That(factory.VariablesTool.Placeholder).IsEqualTo(Placeholders.NoProject);
        });

    [Test]
    public async Task Variables_follow_the_player_and_flag_changes() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;
            var variables = app.Shell.Factory.VariablesTool;

            var torch = variables.Items.Single(i => i.Name == "has_torch");
            await Assert.That(torch.Value).IsEqualTo("false");
            await Assert.That(torch.IsChanged).IsFalse();
            await Assert.That(torch.JustChanged).IsFalse();

            await Choose(player, "forest");
            await Choose(player, "Light a torch");

            torch = variables.Items.Single(i => i.Name == "has_torch");
            await Assert.That(torch.Value).IsEqualTo("true");
            await Assert.That(torch.IsChanged).IsTrue();
            await Assert.That(torch.JustChanged).IsTrue();
            await Assert.That(torch.Detail).IsEqualTo("Started as false");

            // Stepping back replays to before the torch was lit.
            player.StepBackCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 1);
            await Assert.That(variables.Items.Single(i => i.Name == "has_torch").Value).IsEqualTo("false");
        });

    [Test]
    public async Task Filter_and_changed_only_narrow_the_list() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            File.WriteAllText(main, File.ReadAllText(main).Replace("VAR has_torch = false", "VAR has_torch = false\nVAR gold = 5\nVAR player_name = \"Ada\"", StringComparison.Ordinal));
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);
            var variables = app.Shell.Factory.VariablesTool;
            await Assert.That(variables.Items.Select(i => i.Name)).IsEquivalentTo(["gold", "has_torch", "player_name"]);

            variables.Filter = "ada";
            await Assert.That(variables.Items.Select(i => i.Name)).IsEquivalentTo(["player_name"]);

            variables.Filter = string.Empty;
            variables.ChangedOnly = true;
            await Assert.That(variables.Items).IsEmpty();
            await Assert.That(variables.Placeholder).IsEqualTo("No variables match.");
        });

    [Test]
    public async Task Opening_a_variable_jumps_to_its_declaration() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var variables = app.Shell.Factory.VariablesTool;

            variables.OpenCommand.Execute(variables.Items.Single(i => i.Name == "has_torch"));

            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.Caret.Line == 5)).IsTrue();
            await Assert.That(app.Shell.ActiveDocument?.RelativePath).IsEqualTo("main.ink");
        });
}
