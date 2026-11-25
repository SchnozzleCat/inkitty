using Dock.Model.Controls;
using Dock.Model.Core;
using Inkitty.App.Docking;
using Inkitty.App.Tests.Infrastructure;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class DockingTests
{
    private static IEnumerable<IDockable> All(IDockable dockable) =>
        dockable is IDock dock ? [dockable, .. (dock.VisibleDockables ?? []).SelectMany(All)] : [dockable];

    [Test]
    public async Task Default_layout_has_three_columns_with_issues_under_the_editor() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var main = (IProportionalDock)app.Shell.Layout.VisibleDockables!.Single();
            var columns = main.VisibleDockables!.Where(d => d is not IProportionalDockSplitter).ToList();

            await Assert.That(columns.Count).IsEqualTo(3);
            await Assert.That(All(columns[0]).Any(d => d.Id == DockIds.Files)).IsTrue();
            await Assert.That(All(columns[1]).Any(d => d is IDocumentDock)).IsTrue();
            await Assert.That(All(columns[1]).Any(d => d.Id == DockIds.Issues)).IsTrue();
            await Assert.That(All(columns[2]).Any(d => d.Id == DockIds.Player)).IsTrue();
        });

    [Test]
    public async Task Closed_tool_is_hidden_and_restored_from_view_menu() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;

            factory.CloseDockable(factory.PlayerTool);
            await Assert.That(factory.IsToolVisible(factory.PlayerTool)).IsFalse();

            app.Shell.ShowPlayerCommand.Execute(null);
            await Assert.That(factory.IsToolVisible(factory.PlayerTool)).IsTrue();
        });

    [Test]
    public async Task Layout_round_trips_through_layout_json_and_reset_restores_default() =>
        await HeadlessSession.Run(async () =>
        {
            var config = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
            await using (var first = AppFixture.Create(config))
            {
                // Move the Issues tool next to the Files tool.
                var factory = first.Shell.Factory;
                var filesDock = (IDock)factory.FilesTool.Owner!;
                factory.MoveDockable((IDock)factory.IssuesTool.Owner!, filesDock, factory.IssuesTool, factory.FilesTool);
                await Assert.That(factory.IssuesTool.Owner).IsEqualTo(filesDock);
                await first.Shell.OnClosingAsync();
                await Assert.That(first.Get<DockLayoutService>().LastError?.ToString()).IsNull();
            }

            await Assert.That(File.Exists(Path.Join(config, "layout.json"))).IsTrue();

            await using var second = AppFixture.Create(config);
            var restored = second.Shell.Factory;
            await Assert.That(restored.IssuesTool.Owner).IsEqualTo(restored.FilesTool.Owner);

            second.Shell.ResetLayoutCommand.Execute(null);
            await Assert.That(restored.IssuesTool.Owner).IsNotEqualTo(restored.FilesTool.Owner);
            await Assert.That(restored.IssuesTool.Owner?.Id).IsEqualTo(DockIds.BottomDock);
        });

    [Test]
    public async Task Corrupt_layout_falls_back_to_default() =>
        await HeadlessSession.Run(async () =>
        {
            var config = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Join(config, "layout.json"), "{ nope");
            File.WriteAllText(Path.Join(config, "layout.version"), DockLayoutService.LayoutVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

            await using var app = AppFixture.Create(config);
            await Assert.That(app.Shell.Factory.PlayerTool.Owner?.Id).IsEqualTo(DockIds.RightDock);
        });

    [Test]
    public async Task Switching_tabs_keeps_undo_history() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var main = app.Shell.ActiveDocument!;
            main.Document.Insert(0, "// edit\n");

            app.Shell.OpenDocument(app.Project.Project.Find("chapters/town.ink")!);
            await UiTestHelpers.WaitUntil(() => true);
            app.Shell.OpenDocument(main.File);
            await UiTestHelpers.WaitUntil(() => true);

            await Assert.That(main.Document.UndoStack.CanUndo).IsTrue();
            main.Document.UndoStack.Undo();
            await Assert.That(main.Document.Text).DoesNotStartWith("// edit");
        });
}
