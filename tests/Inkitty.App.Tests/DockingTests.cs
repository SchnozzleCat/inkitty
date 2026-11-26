using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Inkitty.App.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Tests.Infrastructure;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class DockingTests
{
    /// <summary>Tools whose pane is on screen: the selected tab of each tool dock (others are behind it).</summary>
    private static HashSet<IDockable> ShownTools(InkittyDockFactory factory) =>
        [.. factory.Tools.Where(t => t.Owner is IDock { ActiveDockable: var active } && active == t)];

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

    [Test]
    public async Task Tool_panes_have_a_header_that_moves_and_closes_them() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;
            await UiTestHelpers.WaitUntil(() =>
            {
                app.Window.UpdateLayout();
                return app.Window.GetVisualDescendants().OfType<PaneGrip>().Count() == ShownTools(factory).Count;
            });

            // Dock's own title bars stay hidden; each shown tool has one Inkitty header showing its title,
            // above the pane's content rather than over it.
            var titleBars = app.Window.GetVisualDescendants().OfType<Avalonia.Controls.Border>()
                .Where(b => b.Name == "PART_Border" && b.TemplatedParent is ToolChromeControl && b.IsEffectivelyVisible);
            await Assert.That(titleBars.Count()).IsEqualTo(0);
            var grips = app.Window.GetVisualDescendants().OfType<PaneGrip>().ToList();
            await Assert.That(grips.Select(g => g.Dockable).ToHashSet().SetEquals(ShownTools(factory))).IsTrue();
            foreach (var header in grips)
            {
                var title = header.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Single(t => t.Name == "PART_Title");
                await Assert.That(title.Text).IsEqualTo(header.Dockable!.Title);
                await Assert.That(header.Bounds.Height).IsGreaterThanOrEqualTo(24);
                var content = ((Avalonia.Controls.DockPanel)header.GetVisualParent()!).Children.Skip(1)
                    .OfType<Avalonia.Controls.Control>().First(c => c.IsVisible);
                await Assert.That(content.Bounds.Top).IsGreaterThanOrEqualTo(header.Bounds.Bottom);
            }

            // Dragging the Player's grip onto the Files pane tabs Player in with Files.
            var grip = grips.Single(g => g.Dockable == factory.PlayerTool);
            var from = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, 4), app.Window)!.Value;
            var filesView = app.Window.GetVisualDescendants().OfType<Views.Tools.FileTreeToolView>().Single();
            var to = filesView.TranslatePoint(new Point(filesView.Bounds.Width / 2, filesView.Bounds.Height / 2), app.Window)!.Value;

            app.Window.MouseDown(from, MouseButton.Left);
            for (var i = 1; i <= 10; i++)
            {
                app.Window.MouseMove(from + ((to - from) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
            }

            app.Window.MouseUp(to, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            app.Window.UpdateLayout();

            await Assert.That(factory.PlayerTool.Owner).IsEqualTo(factory.FilesTool.Owner);

            // The header's close button hides the tool.
            app.Window.UpdateLayout();
            var issues = app.Window.GetVisualDescendants().OfType<PaneGrip>().Single(g => g.Dockable == factory.IssuesTool);
            var close = issues.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == "PART_CloseButton");
            close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Assert.That(factory.IsToolVisible(factory.IssuesTool)).IsFalse();
        });

    [Test]
    public async Task Pane_can_be_docked_at_the_bottom_window_edge() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;
            await UiTestHelpers.WaitUntil(() =>
            {
                app.Window.UpdateLayout();
                return app.Window.GetVisualDescendants().OfType<PaneGrip>().Count() == ShownTools(factory).Count;
            });

            var grip = app.Window.GetVisualDescendants().OfType<PaneGrip>().Single(g => g.Dockable == factory.PlayerTool);
            var from = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, 4), app.Window)!.Value;
            var dockControl = app.Window.GetVisualDescendants().OfType<DockControl>().First();
            var middle = dockControl.TranslatePoint(new Point(dockControl.Bounds.Width / 2, dockControl.Bounds.Height / 2), app.Window)!.Value;

            app.Window.MouseDown(from, MouseButton.Left);
            for (var i = 1; i <= 10; i++)
            {
                app.Window.MouseMove(from + ((middle - from) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
            }

            // The window-edge selector at the bottom sits over the Issues pane's tab strip; it must stay up as it's approached.
            app.Window.UpdateLayout();
            var selector = app.Window.GetVisualDescendants().OfType<Avalonia.Controls.Image>()
                .Single(s => s.Name == "PART_BottomSelector" && s.TemplatedParent is GlobalDockTarget);
            var to = selector.TranslatePoint(new Point(selector.Bounds.Width / 2, selector.Bounds.Height / 2), app.Window)!.Value;
            for (var i = 1; i <= 10; i++)
            {
                app.Window.MouseMove(middle + ((to - middle) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
                app.Window.UpdateLayout();
                await Assert.That(selector.IsEffectivelyVisible).IsTrue();
            }

            app.Window.MouseUp(to, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            app.Window.UpdateLayout();

            // Docked across the whole window, below the old layout.
            var outer = (IProportionalDock)app.Shell.Layout.ActiveDockable!;
            await Assert.That(outer.Orientation).IsEqualTo(Orientation.Vertical);
            await Assert.That(All(outer.VisibleDockables!.Last()).Contains(factory.PlayerTool)).IsTrue();
        });

    [Test]
    public async Task Floating_pane_can_be_docked_at_the_bottom_edge_after_it_stops_moving() =>
        await HeadlessSession.Run(async () =>
        {
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            try
            {
                await using var app = AppFixture.Create();
                var factory = app.Shell.Factory;
                factory.FloatDockable(factory.PlayerTool);
                MdiDocumentWindow? floating = null;
                await UiTestHelpers.WaitUntil(() =>
                {
                    app.Window.UpdateLayout();
                    floating = app.Window.GetVisualDescendants().OfType<MdiDocumentWindow>().FirstOrDefault(w => w.IsEffectivelyVisible);
                    return floating is not null;
                });

                var header = floating!.GetVisualDescendants().OfType<Avalonia.Controls.Control>().First(c => c.Name == "PART_Header");
                var from = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), app.Window)!.Value;
                var dockControl = app.Window.GetVisualDescendants().OfType<DockControl>().First();
                var to = dockControl.TranslatePoint(new Point(dockControl.Bounds.Width / 2, dockControl.Bounds.Height - 20), app.Window)!.Value;

                // The window is stopped by the bottom edge long before the pointer gets there, so the pointer
                // leaves its header and crosses its body on the way to the bottom selector.
                app.Window.MouseDown(from, MouseButton.Left);
                for (var i = 1; i <= 20; i++)
                {
                    app.Window.MouseMove(from + ((to - from) * (i / 20.0)), RawInputModifiers.LeftMouseButton);
                    app.Window.UpdateLayout();
                }

                var selector = app.Window.GetVisualDescendants().OfType<Avalonia.Controls.Image>()
                    .SingleOrDefault(s => s.Name == "PART_BottomSelector" && s.TemplatedParent is GlobalDockTarget);
                await Assert.That(selector?.IsEffectivelyVisible).IsTrue();

                app.Window.MouseUp(to, MouseButton.Left, RawInputModifiers.LeftMouseButton);
                app.Window.UpdateLayout();

                var outer = (IProportionalDock)app.Shell.Layout.ActiveDockable!;
                await Assert.That(outer.Orientation).IsEqualTo(Orientation.Vertical);
                await Assert.That(All(outer.VisibleDockables!.Last()).Contains(factory.PlayerTool)).IsTrue();
            }
            finally
            {
                Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode;
            }
        });

    [Test]
    public async Task Drop_selectors_are_drawn_in_the_theme_colours() =>
        await HeadlessSession.Run(async () =>
        {
            // In-window floating hosts (the Linux and browser default): the drag preview is in the window's tree.
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            using var restore = new Restore(() => Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode);
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { ColorTheme = "light-plus" }));
            var factory = app.Shell.Factory;
            await UiTestHelpers.WaitUntil(() =>
            {
                app.Window.UpdateLayout();
                return app.Window.GetVisualDescendants().OfType<PaneGrip>().Count() == ShownTools(factory).Count;
            });

            var grip = app.Window.GetVisualDescendants().OfType<PaneGrip>().Single(g => g.Dockable == factory.PlayerTool);
            var from = grip.TranslatePoint(new Point(grip.Bounds.Width / 2, 4), app.Window)!.Value;
            var filesView = app.Window.GetVisualDescendants().OfType<Views.Tools.FileTreeToolView>().Single();
            var to = filesView.TranslatePoint(new Point(filesView.Bounds.Width / 2, filesView.Bounds.Height / 2), app.Window)!.Value;
            app.Window.MouseDown(from, MouseButton.Left);
            for (var i = 1; i <= 10; i++)
            {
                app.Window.MouseMove(from + ((to - from) * (i / 10.0)), RawInputModifiers.LeftMouseButton);
            }

            app.Window.UpdateLayout();
            Avalonia.Controls.Image[] Selectors() => [.. app.Window.GetVisualDescendants().OfType<Avalonia.Controls.Image>()
                .Where(i => i.TemplatedParent is DockTargetBase && i.Name?.EndsWith("Selector", StringComparison.Ordinal) == true && i.IsEffectivelyVisible)];
            var selectors = Selectors();
            await Assert.That(selectors.Length).IsGreaterThanOrEqualTo(5);
            await Assert.That(selectors.All(i => i.Source is Avalonia.Media.DrawingImage)).IsTrue();

            // The label following the pointer shows the pane's title in the theme's header colours, and doesn't
            // change size when the drop status changes (Dock doesn't always re-measure it: it'd be cut off).
            var preview = app.Window.GetVisualDescendants().OfType<DragPreviewControl>().Single(p => p.IsEffectivelyVisible);
            await Assert.That(preview.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>().Any(t => t.Text == factory.PlayerTool.Title)).IsTrue();
            var frame = preview.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First();
            Avalonia.Application.Current!.TryGetResource("InkittyPaneHeaderBackground", null, out var headerBrush);
            await Assert.That(frame.Background).IsSameReferenceAs(headerBrush);
            var sizes = new List<Size>();
            foreach (var status in (string[])["Dock", "Float", "None"])
            {
                preview.Status = status;
                preview.InvalidateMeasure();
                preview.Measure(Size.Infinity);
                sizes.Add(preview.DesiredSize);
            }

            await Assert.That(sizes.Distinct().Count()).IsEqualTo(1);

            // A theme switch while dragging swaps them for the new theme's.
            var before = selectors[0].Source;
            app.Settings.Update(s => s with { ColorTheme = "dracula" });
            app.Window.UpdateLayout();
            await Assert.That(Selectors()[0].Source).IsNotSameReferenceAs(before);

            app.Window.MouseUp(from, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        });

    [Test]
    public async Task Empty_floating_window_in_a_saved_layout_is_dropped_on_load() =>
        await HeadlessSession.Run(async () =>
        {
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            using var restore = new Restore(() => Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode);
            var config = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
            await using (var first = AppFixture.Create(config))
            {
                // What closing a floating tool used to leave: its window, empty, the tool hidden inside it.
                var factory = first.Shell.Factory;
                factory.FloatDockable(factory.StoryMapTool);
                await UiTestHelpers.WaitUntil(() => FloatingWindows(first).Length == 1);
                var window = first.Shell.Layout.Windows!.Single();
                var floatingRoot = (IRootDock)window.Layout!;
                var map = factory.StoryMapTool;
                ((IDock)map.Owner!).VisibleDockables!.Remove(map);
                floatingRoot.HiddenDockables ??= new System.Collections.ObjectModel.ObservableCollection<IDockable>();
                floatingRoot.HiddenDockables.Add(map);
                map.Owner = floatingRoot;
                await first.Shell.OnClosingAsync();
                await Assert.That(first.Get<DockLayoutService>().LastError?.ToString()).IsNull();
            }

            await using var second = AppFixture.Create(config);
            await Assert.That(second.Get<DockLayoutService>().LastError?.ToString()).IsNull();
            await Assert.That(second.Shell.Layout.Windows?.Count ?? 0).IsEqualTo(0);
            await Assert.That(FloatingWindows(second).Length).IsEqualTo(0);
            await Assert.That(second.Shell.Factory.IsToolVisible(second.Shell.Factory.StoryMapTool)).IsFalse();

            second.Shell.ShowStoryMapCommand.Execute(null);
            await Assert.That(second.Shell.Factory.IsToolVisible(second.Shell.Factory.StoryMapTool)).IsTrue();
            await Assert.That(second.Shell.Factory.FindRoot(second.Shell.Factory.StoryMapTool)).IsSameReferenceAs(second.Shell.Layout);
        });

    [Test]
    public async Task Layout_with_a_floating_tool_round_trips() =>
        await HeadlessSession.Run(async () =>
        {
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            using var restore = new Restore(() => Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode);
            var config = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
            await using (var first = AppFixture.Create(config))
            {
                first.Shell.Factory.FloatDockable(first.Shell.Factory.StoryMapTool);
                await UiTestHelpers.WaitUntil(() => FloatingWindows(first).Length == 1);
                await first.Shell.OnClosingAsync();
                await Assert.That(first.Get<DockLayoutService>().LastError?.ToString()).IsNull();
            }

            await using var second = AppFixture.Create(config);
            await Assert.That(second.Get<DockLayoutService>().LastError?.ToString()).IsNull();
            var factory = second.Shell.Factory;
            await Assert.That(second.Shell.Layout.Windows?.Count).IsEqualTo(1);
            await Assert.That(factory.FindRoot(factory.StoryMapTool)).IsNotSameReferenceAs(second.Shell.Layout);
            await Assert.That(factory.IsToolVisible(factory.StoryMapTool)).IsTrue();
        });

    [Test]
    public async Task Floating_pane_menus_only_offer_what_panes_support() =>
        await HeadlessSession.Run(async () =>
        {
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            using var restore = new Restore(() => Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode);
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;
            factory.FloatDockable(factory.StoryMapTool);
            await UiTestHelpers.WaitUntil(() => FloatingWindows(app).Length == 1);
            var window = FloatingWindows(app)[0];

            static string[] Headers(IEnumerable<object?>? items) =>
                [.. (items ?? []).OfType<Avalonia.Controls.MenuItem>().Where(i => i.IsVisible).Select(i => i.Header?.ToString() ?? "")];

            // The title bar's right-click menu.
            var header = window.GetVisualDescendants().OfType<Avalonia.Controls.Border>().Single(b => b.Name == "PART_Header");
            var titleMenu = header.ContextMenu!;
            titleMenu.Open(header);
            titleMenu.UpdateLayout();
            await Assert.That(Headers(titleMenu.Items)).IsEquivalentTo(["_Close"]);
            titleMenu.Close();

            // The ⋯ menu on the title bar.
            var menuButton = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == "PART_ToolMenuButton");
            var flyout = (Avalonia.Controls.MenuFlyout)menuButton.Flyout!;
            flyout.ShowAt(menuButton);
            app.Window.UpdateLayout();
            await Assert.That(Headers(flyout.Items)).IsEquivalentTo(["_Close"]);
            var closeItem = (Avalonia.Controls.MenuItem)flyout.Items[0]!;
            await Assert.That(closeItem.CommandParameter).IsSameReferenceAs(factory.StoryMapTool);
            var closeCommand = closeItem.Command!;
            flyout.Hide();

            // No pin (auto hide) button.
            var pin = window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == "PART_ToolPinButton");
            await Assert.That(pin.IsEffectivelyVisible).IsFalse();

            // The menu's Close closes the tool (and its window), as the title bar's close button does.
            closeCommand.Execute(factory.StoryMapTool);
            await Assert.That(factory.IsToolVisible(factory.StoryMapTool)).IsFalse();
            await Assert.That(FloatingWindows(app).Length).IsEqualTo(0);
        });

    [Test]
    public async Task Tool_tab_menu_offers_float_auto_hide_and_close_only() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;

            // Issues and Variables share a pane, so they have tabs.
            ToolTabStripItem? tab = null;
            await UiTestHelpers.WaitUntil(() =>
            {
                app.Window.UpdateLayout();
                tab = app.Window.GetVisualDescendants().OfType<ToolTabStripItem>().FirstOrDefault(t => ReferenceEquals(t.DataContext, factory.VariablesTool));
                return tab is not null;
            });
            var host = tab!.GetVisualDescendants().OfType<Avalonia.Controls.Border>().First(b => b.ContextMenu is not null);
            var menu = host.ContextMenu!;
            menu.Open(host);
            menu.UpdateLayout();
            var headers = menu.Items.OfType<Avalonia.Controls.MenuItem>().Where(i => i.IsVisible).Select(i => i.Header?.ToString() ?? "").ToArray();
            await Assert.That(headers).IsEquivalentTo(["_Float", "_Auto Hide", "_Close"]);
            menu.Close();

            // Once floating, Float isn't offered again.
            factory.FloatDockable(factory.VariablesTool);
            static bool InMain(IDockable d) => (bool)DockConverters.IsInMainWindow.Convert(d, typeof(bool), null, System.Globalization.CultureInfo.InvariantCulture)!;
            await Assert.That(InMain(factory.VariablesTool)).IsFalse();
            await Assert.That(InMain(factory.IssuesTool)).IsTrue();
        });

    private static PaneGrip HeaderOf(AppFixture app, IDockable tool)
    {
        app.Window.UpdateLayout();
        return app.Window.GetVisualDescendants().OfType<PaneGrip>().First(g => g.Dockable == tool && g.IsEffectivelyVisible);
    }

    private static Avalonia.Controls.Button PinButtonOf(PaneGrip header) =>
        header.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == "PART_PinButton");

    private static void Click(Avalonia.Controls.Button button) =>
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));

    [Test]
    public async Task Pane_header_pin_auto_hides_the_pane_and_its_header_unpins_it() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;
            var files = factory.FilesTool;
            var home = files.Owner;
            await UiTestHelpers.WaitUntil(() => { app.Window.UpdateLayout(); return app.Window.GetVisualDescendants().OfType<PaneGrip>().Any(g => g.Dockable == files); });

            // Docked: the header offers pinning.
            var header = HeaderOf(app, files);
            await Assert.That(header.Classes.Contains(":can-pin")).IsTrue();
            Click(PinButtonOf(header));
            await Assert.That(factory.IsDockablePinned(files)).IsTrue();
            await Assert.That(app.Shell.Layout.LeftPinnedDockables).Contains(files);

            // It's on the window edge as a tab...
            app.Window.UpdateLayout();
            await Assert.That(app.Window.GetVisualDescendants().OfType<ToolPinItemControl>().Any(i => i.DataContext == files)).IsTrue();

            // ...View > Files slides it out, and that header's pin (filled) unpins it back into its dock.
            app.Shell.ShowFilesCommand.Execute(null);
            await UiTestHelpers.WaitUntil(() => { app.Window.UpdateLayout(); return app.Window.GetVisualDescendants().OfType<PaneGrip>().Any(g => g.Dockable == files && g.IsEffectivelyVisible); });
            header = HeaderOf(app, files);
            await Assert.That(header.Classes.Contains(":pinned")).IsTrue();
            await Assert.That(PinButtonOf(header).IsEffectivelyVisible).IsTrue();
            Click(PinButtonOf(header));
            await Assert.That(factory.IsDockablePinned(files)).IsFalse();
            await Assert.That(files.Owner).IsSameReferenceAs(home);
        });

    [Test]
    public async Task Edge_tab_menu_unpins_an_auto_hidden_pane() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;
            var issues = factory.IssuesTool;
            var home = issues.Owner;
            factory.PinDockable(issues);
            ToolPinItemControl? tab = null;
            await UiTestHelpers.WaitUntil(() =>
            {
                app.Window.UpdateLayout();
                tab = app.Window.GetVisualDescendants().OfType<ToolPinItemControl>().FirstOrDefault(i => i.DataContext == issues);
                return tab is not null;
            });
            await Assert.That(tab).IsNotNull();
            var host = tab!.GetVisualDescendants().OfType<Avalonia.Controls.Control>().First(c => c.ContextMenu is not null);
            var menu = host.ContextMenu!;
            menu.Open(host);
            menu.UpdateLayout();
            var items = menu.Items.OfType<Avalonia.Controls.MenuItem>().Where(i => i.IsVisible).ToList();
            await Assert.That(items.Select(i => i.Header?.ToString() ?? "")).IsEquivalentTo(["_Show", "_Unpin", "_Close"]);
            var unpin = items.Single(i => i.Header?.ToString() == "_Unpin");
            unpin.Command!.Execute(unpin.CommandParameter);
            menu.Close();

            await Assert.That(factory.IsDockablePinned(issues)).IsFalse();
            await Assert.That(issues.Owner).IsSameReferenceAs(home);
        });

    [Test]
    public async Task Floating_window_allows_pinning_only_with_more_than_one_pane() =>
        await HeadlessSession.Run(async () =>
        {
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            using var restore = new Restore(() => Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode);
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;
            var map = factory.StoryMapTool;
            factory.FloatDockable(map);
            await UiTestHelpers.WaitUntil(() => FloatingWindows(app).Length == 1);

            // Alone in the window: no pinning, not even through the factory.
            await Assert.That(factory.CanPinTool(map)).IsFalse();
            factory.PinDockable(map);
            await Assert.That(factory.IsDockablePinned(map)).IsFalse();
            var window = FloatingWindows(app)[0];
            await Assert.That(window.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Single(b => b.Name == "PART_ToolPinButton").IsEffectivelyVisible).IsFalse();

            // Split a second pane into it: each pane gets its own header, with a pin.
            var mapDock = (IDock)map.Owner!;
            var playerDock = new Dock.Model.Mvvm.Controls.ToolDock { Alignment = Alignment.Right, VisibleDockables = factory.CreateList<IDockable>() };
            factory.RemoveDockable(factory.PlayerTool, true);
            factory.AddDockable(playerDock, factory.PlayerTool);
            playerDock.ActiveDockable = factory.PlayerTool;
            factory.SplitToDock(mapDock, playerDock, DockOperation.Right);
            await Assert.That(factory.CanPinTool(map)).IsTrue();
            await UiTestHelpers.WaitUntil(() => { app.Window.UpdateLayout(); return app.Window.GetVisualDescendants().OfType<PaneGrip>().Any(g => g.Dockable == map && g.IsEffectivelyVisible); });
            var header = HeaderOf(app, map);
            await Assert.That(header.Classes.Contains(":can-pin")).IsTrue();
            Click(PinButtonOf(header));
            await Assert.That(factory.IsDockablePinned(map)).IsTrue();

            // Unpinning puts it back into the floating window.
            var floatingRoot = factory.FindRoot(map, _ => true)!;
            factory.PinDockable(map);
            await Assert.That(factory.IsDockablePinned(map)).IsFalse();
            await Assert.That(factory.FindRoot(map, _ => true)).IsSameReferenceAs(floatingRoot);
        });

    [Test]
    public async Task Auto_hidden_pane_stays_auto_hidden_across_restarts() =>
        await HeadlessSession.Run(async () =>
        {
            var config = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
            await using (var first = AppFixture.Create(config))
            {
                first.Shell.Factory.PinDockable(first.Shell.Factory.OutlineTool);
                first.Shell.Factory.PreviewPinnedDockable(first.Shell.Factory.OutlineTool); // slid out when closing
                await first.Shell.OnClosingAsync();
                await Assert.That(first.Get<DockLayoutService>().LastError?.ToString()).IsNull();
            }

            await using var second = AppFixture.Create(config);
            var factory = second.Shell.Factory;
            await Assert.That(second.Get<DockLayoutService>().LastError?.ToString()).IsNull();
            await Assert.That(factory.IsDockablePinned(factory.OutlineTool)).IsTrue();
            factory.PinDockable(factory.OutlineTool);
            await Assert.That(factory.IsDockablePinned(factory.OutlineTool)).IsFalse();
            await Assert.That(factory.IsInMainWindow(factory.OutlineTool)).IsTrue();
        });

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private static MdiDocumentWindow[] FloatingWindows(AppFixture app)
    {
        app.Window.UpdateLayout();
        return [.. app.Window.GetVisualDescendants().OfType<MdiDocumentWindow>().Where(w => w.IsEffectivelyVisible)];
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Closing_a_floating_tool_removes_its_window_and_view_menu_brings_it_back(bool closeFromWindowTitleBar) =>
        await HeadlessSession.Run(async () =>
        {
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            using var restore = new Restore(() => Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode);
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;
            var map = factory.StoryMapTool;

            factory.FloatDockable(map);
            await UiTestHelpers.WaitUntil(() => FloatingWindows(app).Length == 1);

            if (closeFromWindowTitleBar)
            {
                // The floating window's own close button (Dock's title bar).
                var close = FloatingWindows(app)[0].GetVisualDescendants().OfType<Avalonia.Controls.Button>()
                    .Single(b => b.Name == "PART_ToolCloseButton");
                close.Command!.Execute(close.CommandParameter);
            }
            else
            {
                factory.CloseDockable(map);
            }

            await Assert.That(factory.IsToolVisible(map)).IsFalse();
            await Assert.That(FloatingWindows(app).Length).IsEqualTo(0);
            await Assert.That(app.Shell.Layout.Windows?.Count ?? 0).IsEqualTo(0);

            // View > Map shows it again, docked in the main window.
            app.Shell.ShowStoryMapCommand.Execute(null);
            await Assert.That(factory.IsToolVisible(map)).IsTrue();
            await Assert.That(factory.FindRoot(map)).IsSameReferenceAs(app.Shell.Layout);
            await Assert.That(FloatingWindows(app).Length).IsEqualTo(0);
            var shown = await UiTestHelpers.WaitUntil(() =>
            {
                app.Window.UpdateLayout();
                return app.Window.GetVisualDescendants().OfType<Views.Tools.StoryMapToolView>().Any(v => v.IsEffectivelyVisible);
            });
            await Assert.That(shown).IsTrue();
        });

    [Test]
    public async Task Closed_floating_tool_returns_to_its_old_dock_if_it_is_still_there() =>
        await HeadlessSession.Run(async () =>
        {
            var hostMode = Dock.Settings.DockSettings.FloatingWindowHostMode;
            Dock.Settings.DockSettings.FloatingWindowHostMode = DockFloatingWindowHostMode.Managed;
            using var restore = new Restore(() => Dock.Settings.DockSettings.FloatingWindowHostMode = hostMode);
            await using var app = AppFixture.Create();
            var factory = app.Shell.Factory;

            // Variables shares the bottom dock with Issues, so that dock stays when Variables floats.
            var bottom = (IDock)factory.VariablesTool.Owner!;
            factory.FloatDockable(factory.VariablesTool);
            await UiTestHelpers.WaitUntil(() => FloatingWindows(app).Length == 1);
            factory.CloseDockable(factory.VariablesTool);
            await Assert.That(FloatingWindows(app).Length).IsEqualTo(0);

            app.Shell.ShowVariablesCommand.Execute(null);
            await Assert.That(factory.VariablesTool.Owner).IsSameReferenceAs(bottom);
            await Assert.That(bottom.ActiveDockable).IsSameReferenceAs(factory.VariablesTool);
        });
}
