using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Inkitty.App.Tests.Infrastructure;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class MenuTests
{
    [Test]
    public async Task Every_menu_opens_without_throwing_and_recent_items_open_projects() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            await using var app = AppFixture.Create(configure: s => s.Update(x => x.WithRecentProject(main)));

            var menu = app.Window.GetLogicalDescendants().OfType<Menu>().Single();
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                item.Open();
                await UiTestHelpers.WaitUntil(() => true);
                item.Close();
            }

            var recent = app.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Name == "RecentMenu");
            await Assert.That(recent.Command).IsNull();
            var file = (MenuItem)recent.Parent!;
            file.Open();
            await UiTestHelpers.WaitUntil(() => true);
            recent.Open();
            await UiTestHelpers.WaitUntil(() => recent.ContainerFromIndex(0) is MenuItem);
            var entry = (MenuItem)recent.ContainerFromIndex(0)!;
            await Assert.That(entry.CommandParameter).IsEqualTo(main);

            entry.Command!.Execute(entry.CommandParameter);
            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.Project is not null)).IsTrue();
            recent.Close();
            file.Close();
        });

    [Test]
    public async Task Clicking_a_recent_project_closes_the_menu() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            await using var app = AppFixture.Create(configure: s => s.Update(x => x.WithRecentProject(main)));

            var recent = app.Window.GetLogicalDescendants().OfType<MenuItem>().Single(m => m.Name == "RecentMenu");
            var file = (MenuItem)recent.Parent!;
            file.Open();
            await UiTestHelpers.WaitUntil(() => true);
            recent.Open();
            await UiTestHelpers.WaitUntil(() => recent.ContainerFromIndex(0) is MenuItem { IsEffectivelyVisible: true });
            var entry = (MenuItem)recent.ContainerFromIndex(0)!;
            entry.UpdateLayout();

            var root = TopLevel.GetTopLevel(entry)!;
            var point = entry.TranslatePoint(new Point(entry.Bounds.Width / 2, entry.Bounds.Height / 2), root)!.Value;
            if (root is Window window)
            {
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
            }
            else
            {
                throw new InvalidOperationException($"menu root is {root.GetType()}");
            }

            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.Project is not null)).IsTrue();
            await UiTestHelpers.WaitUntil(() => !file.IsSubMenuOpen, 1000);
            await Assert.That(recent.IsSubMenuOpen).IsFalse();
            await Assert.That(file.IsSubMenuOpen).IsFalse();
        });
}
