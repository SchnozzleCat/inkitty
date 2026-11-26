using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Inkitty.App.Services;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.Views;
using Inkitty.Core.Workspace;

namespace Inkitty.App.Tests;

/// <summary>Behaviour the browser host relies on: in-view modals, the workspace dialogs and export.</summary>
[NotInParallel("ui")]
public class WebHostTests
{
    private static IEnumerable<Button> Buttons(Control root) =>
        root.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible);

    private static Button ButtonLabelled(Control root, string label) =>
        Buttons(root).First(b => b.Content as string == label);

    private static async Task<T> InOverlays<T>(Func<Task<T>> body)
    {
        MainView.UseDialogWindows = false;
        try
        {
            return await body();
        }
        finally
        {
            MainView.UseDialogWindows = true;
        }
    }

    [Test]
    public async Task Overlay_modals_block_the_shell_stack_and_close_with_escape() =>
        await HeadlessSession.Run(() => InOverlays(async () =>
        {
            await using var app = AppFixture.Create();
            var view = app.Window.MainView;
            var shell = view.FindControl<DockPanel>("Shell")!;

            var outer = view.ShowModalAsync("Outer", new Button { Content = "Outer button" });
            await UiTestHelpers.WaitUntil(() => view.IsModalOpen);
            var inner = view.ShowModalAsync("Inner", new Button { Content = "Inner button" });
            await UiTestHelpers.WaitUntil(() => Buttons(view).Any(b => b.Content as string == "Inner button"));

            await Assert.That(shell.IsEnabled).IsFalse();
            await Assert.That(ButtonLabelled(view, "Outer button").IsEffectivelyEnabled).IsFalse();
            await Assert.That(ButtonLabelled(view, "Inner button").IsEffectivelyEnabled).IsTrue();

            ButtonLabelled(view, "Inner button").Focus();
            app.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await UiTestHelpers.WaitUntil(() => inner.IsCompleted);
            await Assert.That(inner.IsCompleted).IsTrue();
            await Assert.That(outer.IsCompleted).IsFalse();
            await Assert.That(ButtonLabelled(view, "Outer button").IsEffectivelyEnabled).IsTrue();

            view.CloseModal();
            await UiTestHelpers.WaitUntil(() => outer.IsCompleted);
            await Assert.That(shell.IsEnabled).IsTrue();
            await Assert.That(view.IsModalOpen).IsFalse();
            return true;
        }));

    [Test]
    public async Task Settings_open_as_an_overlay_and_close() =>
        await HeadlessSession.Run(() => InOverlays(async () =>
        {
            await using var app = AppFixture.Create();
            var view = app.Window.MainView;

            app.Shell.OpenSettingsCommand.Execute(null);
            await Assert.That(await UiTestHelpers.WaitUntil(() => view.GetVisualDescendants().OfType<SettingsView>().Any())).IsTrue();

            ButtonLabelled(view, "Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.That(await UiTestHelpers.WaitUntil(() => !view.IsModalOpen)).IsTrue();
            return true;
        }));

    [Test]
    public async Task Message_dialogs_return_the_clicked_button() =>
        await HeadlessSession.Run(() => InOverlays(async () =>
        {
            await using var app = AppFixture.Create();
            var view = app.Window.MainView;
            var dialogs = new DialogService();

            var answer = dialogs.AskUnsavedChangesAsync("main.ink");
            await UiTestHelpers.WaitUntil(() => view.IsModalOpen);
            ButtonLabelled(view, "Don't save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UiTestHelpers.WaitUntil(() => answer.IsCompleted);

            await Assert.That(await answer).IsEqualTo(UnsavedChangesChoice.Discard);
            return true;
        }));

    [Test]
    public async Task Workspace_dialogs_create_list_open_and_delete_projects() =>
        await HeadlessSession.Run(() => InOverlays(async () =>
        {
            await using var app = AppFixture.Create();
            var view = app.Window.MainView;
            var root = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
            var dialogs = new WorkspaceDialogService(new ProjectWorkspace(root));

            // New project: accept the suggested name.
            var created = dialogs.PickNewProjectFileAsync();
            await UiTestHelpers.WaitUntil(() => view.IsModalOpen);
            ButtonLabelled(view, "Create").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UiTestHelpers.WaitUntil(() => created.IsCompleted);
            var main = await created;
            await Assert.That(main).IsEqualTo(Path.Join(root, "story", "main.ink"));
            await Assert.That(File.ReadAllText(main!)).Contains("Once upon a time");

            await dialogs.Workspace.CreateAsync("second", "Second");

            // Open: both are listed; delete one (confirming), then open the other.
            var picked = dialogs.PickInkFileToOpenAsync();
            await UiTestHelpers.WaitUntil(() => Buttons(view).Count(b => b.Content as string == "Delete") == 2);
            Buttons(view).First(b => b.Content as string == "Delete" && ToolTip.GetTip(b) as string == "Delete second")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UiTestHelpers.WaitUntil(() => Buttons(view).Any(b => b.Content as string == "Cancel" && b.IsEffectivelyEnabled) && Buttons(view).Count(b => b.Content as string == "Delete") == 3);
            Buttons(view).Last(b => b.Content as string == "Delete").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UiTestHelpers.WaitUntil(() => Buttons(view).Count(b => b.Content as string == "Delete") == 1);
            await Assert.That(dialogs.Workspace.List().Select(p => p.Name)).IsEquivalentTo(["story"]);

            Buttons(view).First(b => b.Classes.Contains("project-row")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UiTestHelpers.WaitUntil(() => picked.IsCompleted);
            await Assert.That(await picked).IsEqualTo(main);
            await Assert.That(view.IsModalOpen).IsFalse();
            return true;
        }));

    [Test]
    public async Task Export_saves_a_zip_of_the_project_including_unsaved_edits() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);
            app.Project.Project.Main.SetText(app.Project.Project.Main.Text + "\nEdited.");

            await app.Shell.ExportProjectCommand.ExecuteAsync(null);

            var (name, contents) = app.Dialogs.SavedCopies.Single();
            await Assert.That(name).IsEqualTo("main.zip");
            using var zip = new ZipArchive(new MemoryStream(contents));
            await Assert.That(zip.Entries.Select(e => e.FullName)).IsEquivalentTo(["chapters/forest.ink", "chapters/town.ink", "main.ink"]);
            using var reader = new StreamReader(zip.GetEntry("main.ink")!.Open());
            await Assert.That(reader.ReadToEnd()).EndsWith("Edited.");
        });

    [Test]
    public async Task A_project_namer_names_the_files_pane_title_and_export() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            await using var app = AppFixture.Create(projectNamer: _ => "My story");
            await app.OpenAsync(main);

            await app.Shell.ExportProjectCommand.ExecuteAsync(null);

            await Assert.That(app.Project.Name).IsEqualTo("My story");
            await Assert.That(app.Shell.Factory.FilesTool.ProjectName).IsEqualTo("My story");
            await Assert.That(app.Dialogs.SavedCopies.Single().Name).IsEqualTo("My story.zip");
        });
}
