using Inkitty.App.Docking;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.ViewModels.Tools;
using Inkitty.Core.Outline;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class OutlineTests
{
    private static IEnumerable<OutlineNode> All(IEnumerable<OutlineNode> nodes) =>
        nodes.SelectMany(n => (IEnumerable<OutlineNode>)[n, .. All(n.Children)]);

    [Test]
    public async Task Outline_sits_under_files_and_files_no_longer_outline() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var factory = app.Shell.Factory;

            await Assert.That(factory.OutlineTool.Owner?.Owner?.Id).IsEqualTo(DockIds.LeftColumn);
            await Assert.That(factory.FilesTool.Owner?.Owner?.Id).IsEqualTo(DockIds.LeftColumn);

            // Files lists files (and folders) only.
            var fileNodes = factory.FilesTool.Nodes.SelectMany(n => (IEnumerable<FileNode>)[n, .. n.Children]);
            await Assert.That(fileNodes.All(n => n.IsFile || n.IsFolder || n.IsHeader || n.IsUnincluded)).IsTrue();
            await Assert.That(fileNodes.Single(n => n.Label == "main.ink").Children).IsEmpty();
        });

    [Test]
    public async Task Outline_follows_the_active_file() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var outline = app.Shell.Factory.OutlineTool;

            await Assert.That(outline.FileName).IsEqualTo("main.ink");
            await Assert.That(outline.Nodes.Select(n => (n.Item.Kind, n.Name)))
                .IsEquivalentTo([(OutlineKind.Include, "chapters/forest.ink"), (OutlineKind.Include, "chapters/town.ink"),
                    (OutlineKind.Variable, "has_torch"), (OutlineKind.Constant, "MAX_COURAGE"),
                    (OutlineKind.Knot, "start"), (OutlineKind.Knot, "light_torch")]);
            var start = outline.Nodes.Single(n => n.Name == "start");
            await Assert.That(start.IsExpanded).IsTrue();
            await Assert.That(start.Children.Select(c => c.Name)).Contains("Go left, into the forest");
            var torch = start.Children.Single(c => c.Name == "Light the torch first");
            await Assert.That(torch.Condition).IsEqualTo("{has_torch}");
            await Assert.That(torch.HasDetail).IsFalse();

            app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);
            await Assert.That(outline.FileName).IsEqualTo("forest.ink");
            var forest = outline.Nodes.Single(n => n.Name == "forest");
            await Assert.That(forest.Children.Select(c => c.Name)).IsEquivalentTo(["Light a torch", "Turn back", "clearing"]);
            await Assert.That(forest.Children.Single(c => c.Name == "Light a torch").Detail).IsEqualTo("(light)");
            await Assert.That(outline.Nodes.Select(n => n.Item.Kind)).Contains(OutlineKind.Function);
        });

    [Test]
    public async Task Outline_updates_as_you_type_and_keeps_folds() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var outline = app.Shell.Factory.OutlineTool;
            outline.Nodes.Single(n => n.Name == "start").IsExpanded = false;

            var main = app.Shell.ActiveDocument!;
            main.Document.Insert(main.Document.TextLength, "\n=== epilogue ===\nThe end.\n");

            await Assert.That(await UiTestHelpers.WaitUntil(() => outline.Nodes.Any(n => n.Name == "epilogue"))).IsTrue();
            await Assert.That(outline.Nodes.Single(n => n.Name == "start").IsExpanded).IsFalse();
        });

    [Test]
    public async Task Clicking_jumps_and_the_caret_selects() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var outline = app.Shell.Factory.OutlineTool;
            var main = app.Shell.ActiveDocument!;

            var lightTorch = outline.Nodes.Single(n => n.Name == "light_torch");
            outline.OpenCommand.Execute(lightTorch);
            await Assert.That(await UiTestHelpers.WaitUntil(() => main.Caret.Line == 17)).IsTrue();
            await Assert.That(outline.SelectedNode).IsSameReferenceAs(lightTorch);

            // Moving the caret into a choice selects it.
            main.Caret = new Inkitty.Core.Symbols.SourcePosition(14, 3);
            await Assert.That(outline.SelectedNode?.Name).IsEqualTo("Go right, towards the town");

            outline.ShowWeave = false;
            await Assert.That(All(outline.Nodes).Any(n => n.Item.IsWeave)).IsFalse();
            await Assert.That(outline.SelectedNode?.Name).IsEqualTo("start");
        });
}
