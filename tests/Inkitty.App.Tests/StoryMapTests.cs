using Avalonia.VisualTree;
using Inkitty.App.Docking;
using Inkitty.App.StoryMap;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.ViewModels.Tools;
using Inkitty.Core.StoryMap;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class StoryMapTests
{
    private static async Task Choose(PlayerToolViewModel player, string text)
    {
        player.ChooseCommand.Execute(player.Choices.Single(c => c.Text.Contains(text, StringComparison.Ordinal)));
        await UiTestHelpers.WaitUntil(() => !player.IsBusy);
    }

    private static StoryMapControl? MapControl(AppFixture app) =>
        app.Window.GetVisualDescendants().OfType<StoryMapControl>().FirstOrDefault();

    [Test]
    public async Task Story_map_sits_under_the_player_by_default() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;

            await Assert.That(factory.IsToolVisible(factory.StoryMapTool)).IsTrue();
            await Assert.That(factory.StoryMapTool.Owner?.Owner?.Id).IsEqualTo(DockIds.RightColumn);
            await Assert.That(factory.PlayerTool.Owner?.Owner?.Id).IsEqualTo(DockIds.RightColumn);
        });

    [Test]
    public async Task Map_shows_the_story_and_follows_the_player() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;
            var map = app.Shell.Factory.StoryMapTool;

            await Assert.That(map.Graph.Nodes.Select(n => n.Id)).Contains("forest.clearing");
            await Assert.That(map.Progress.CurrentNode).IsEqualTo("start");
            await Assert.That(player.LocationText).IsEqualTo("start");

            await Choose(player, "forest");
            await Choose(player, "Light a torch");

            // forest -> clearing -> town.square, with the silent "town" knot skipped (it's diverted past).
            await Assert.That(map.Progress.CurrentNode).IsEqualTo("town.square");
            await Assert.That(player.LocationText).IsEqualTo("town › square");
            await Assert.That(map.Progress.TookEdge("forest", "forest.clearing")).IsTrue();
            await Assert.That(map.Progress.WasVisited("forest.clearing")).IsTrue();
            await Assert.That(map.Progress.WasVisited("light_torch")).IsFalse();
            await Assert.That(map.Summary).IsEqualTo("5 of 6 visited"); // all but light_torch ("town" is passed through)

            // The control renders the same graph and progress.
            var control = MapControl(app)!;
            await Assert.That(await UiTestHelpers.WaitUntil(() => control.MapLayout.Nodes.Count == map.Graph.Nodes.Count)).IsTrue();
            await Assert.That(control.Progress).IsSameReferenceAs(map.Progress);
        });

    [Test]
    public async Task Progress_survives_an_edit_and_replay() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;
            await Choose(player, "forest");

            var main = app.Shell.ActiveDocument!;
            await app.WaitForCompileAfter(() => main.Document.Insert(0, "// edited\n"));

            await Assert.That(app.Shell.Factory.StoryMapTool.Progress.CurrentNode).IsEqualTo("forest");
        });

    [Test]
    public async Task Clicking_a_knot_opens_it_in_the_editor() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));

            app.Shell.Factory.StoryMapTool.OpenNodeCommand.Execute("town.square");
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.RelativePath == "chapters/town.ink");

            await Assert.That(app.Shell.ActiveDocument?.RelativePath).IsEqualTo("chapters/town.ink");
            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.Caret.Line == 4)).IsTrue();
        });

    [Test]
    public async Task Editor_margin_marks_the_playthrough_in_each_file() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;
            var main = app.Shell.ActiveDocument!;

            await Assert.That(main.Playthrough.CurrentLine).IsEqualTo(11);
            await Assert.That(main.Playthrough.ChoiceLines).IsEquivalentTo([13, 14]);

            await Choose(player, "forest");
            var forest = app.Project.FindDocument("chapters/forest.ink")!;

            await Assert.That(main.Playthrough.CurrentLine).IsNull();
            await Assert.That(main.Playthrough.VisitedLines).Contains(13); // the choice taken
            await Assert.That(forest.Playthrough.CurrentLine).IsEqualTo(2);
            await Assert.That(forest.Playthrough.ChoiceLines).IsEquivalentTo([3, 7]);

            // The margin is drawn from the same data, and can be switched off.
            var editor = app.EditorFor(main)!;
            await Assert.That(editor.Playthrough.Progress.VisitedLines).Contains(13);
            app.Settings.Update(s => s with { ShowPlaythroughInEditor = false });
            await UiTestHelpers.WaitUntil(() => editor.Playthrough.Progress.IsEmpty);
            await Assert.That(editor.Playthrough.Progress.IsEmpty).IsTrue();
        });

    [Test]
    public async Task Show_current_line_jumps_to_where_the_story_is() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;
            await Choose(player, "forest");

            app.Shell.RevealStoryPositionCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.RelativePath == "chapters/forest.ink");

            await Assert.That(app.Shell.ActiveDocument?.RelativePath).IsEqualTo("chapters/forest.ink");
            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.Caret.Line == 2)).IsTrue();

            // Each transcript line can be revealed too.
            var paragraph = player.Items.OfType<PlayerParagraph>().First();
            await Assert.That(paragraph.SourceText).IsEqualTo("main.ink:11");
        });

    [Test]
    public async Task Layout_engine_routes_every_edge_inside_the_map() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var graph = app.Shell.Factory.StoryMapTool.Graph;
            var sizes = new StoryMapLayoutEngine.Measurements(
                graph.Nodes.ToDictionary(n => n.Id, _ => new Avalonia.Size(80, 30)),
                graph.Edges.Select(e => e.Label).OfType<string>().Distinct().ToDictionary(l => l, _ => new Avalonia.Size(60, 14)));

            var layout = StoryMapLayoutEngine.Compute(graph, sizes);
            var bounds = new Avalonia.Rect(layout.Size).Inflate(1);

            await Assert.That(layout.Nodes.Count).IsEqualTo(graph.Nodes.Count);
            await Assert.That(layout.Edges.Count).IsEqualTo(graph.Edges.Count);
            await Assert.That(layout.Nodes.All(n => bounds.Contains(n.Bounds))).IsTrue();
            await Assert.That(layout.Edges.All(e => e.Route.Count > 0 && bounds.Contains(e.ArrowTip))).IsTrue();
            await Assert.That(layout.Find(StoryGraph.StartId)!.Bounds.Top).IsLessThan(layout.Find(StoryGraph.EndId)!.Bounds.Top);
        });

    [Test]
    public async Task Transcript_links_survive_step_back_and_restart() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var player = app.Shell.Factory.PlayerTool;

            async Task AllLinked(string when)
            {
                await Assert.That(player.Items.OfType<PlayerParagraph>().All(p => p.HasSource)).IsTrue().Because($"paragraphs after {when}");
                await Assert.That(player.Items.OfType<PlayerChoiceEcho>().All(c => c.HasSource)).IsTrue().Because($"choices after {when}");
                await Assert.That(app.Shell.Factory.StoryMapTool.Progress.CurrentNode).IsNotNull().Because($"map after {when}");
            }

            await Choose(player, "forest");
            await Choose(player, "Light a torch");
            for (var i = 0; i < 3; i++)
            {
                // Step back straight after a choice, while the session that played it may still be in use.
                player.StepBackCommand.Execute(null);
                await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 1);
                await AllLinked($"step back {i}");
                await Choose(player, "Light a torch");
                await AllLinked($"choosing again {i}");
            }

            player.RestartCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => !player.IsBusy && player.ChoiceCount == 0);
            await AllLinked("restart");
        });

    [Test]
    public async Task Conditional_edges_show_their_condition_and_are_dashed() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var graph = app.Shell.Factory.StoryMapTool.Graph;

            // main.ink: * {has_torch} [Light the torch first] -> light_torch
            var torch = graph.Edges.Single(e => e.To == "light_torch");
            await Assert.That(torch.Condition).IsEqualTo("{has_torch}");
            await Assert.That(torch.Label).IsEqualTo("Light the torch first");
            var plain = graph.Edges.Single(e => e.From == "start" && e.To == "forest");
            await Assert.That(plain.IsConditional).IsFalse();

            var control = MapControl(app)!;
            await Assert.That(await UiTestHelpers.WaitUntil(() => control.MapLayout.Edges.Count == graph.Edges.Count)).IsTrue();
            var drawnTorch = control.MapLayout.Edges.Single(e => e.To == "light_torch");
            var drawnPlain = control.MapLayout.Edges.Single(e => e.From == "start" && e.To == "forest");

            await Assert.That(drawnTorch.Condition).IsEqualTo("{has_torch}");
            await Assert.That(drawnTorch.LabelCenter).IsNotNull();
            await Assert.That(StoryMapControl.DashFor(drawnTorch)).IsNotNull();
            await Assert.That(StoryMapControl.DashFor(drawnPlain)).IsNull();
        });
}
