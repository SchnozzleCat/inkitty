using Inkitty.App.Tests.Infrastructure;
using Inkitty.Core.Settings;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class ProjectBehaviourTests
{
    [Test]
    public async Task Typing_an_include_creates_the_file_and_save_writes_it() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);

            var doc = app.Shell.ActiveDocument!;
            await app.WaitForCompileAfter(() => doc.Document.Insert(0, "INCLUDE chapters/new.ink\n"));

            await Assert.That(app.Project.Project.Find("chapters/new.ink")).IsNotNull();
            await Assert.That(app.Project.Files.Select(f => f.RelativePath)).Contains("chapters/new.ink");
            await Assert.That(File.Exists(Path.Join(root, "chapters", "new.ink"))).IsFalse();

            await app.Shell.SaveAllCommand.ExecuteAsync(null);
            await Assert.That(File.Exists(Path.Join(root, "chapters", "new.ink"))).IsTrue();
        });

    [Test]
    public async Task External_change_reloads_clean_file_and_prompts_for_dirty_file() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);
            var town = app.Shell.OpenDocument(app.Project.Project.Find("chapters/town.ink")!);
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);

            // Clean file: silently reloaded.
            File.WriteAllText(Path.Join(root, "chapters", "town.ink"), "=== town ===\n-> square\n= square\nChanged outside.\n-> END\n");
            // Dirty file: info bar.
            forest.Document.Insert(0, "// local edit\n");
            File.WriteAllText(Path.Join(root, "chapters", "forest.ink"), "=== forest ===\nOutside edit.\n-> END\n");

            await app.Project.RescanAllAsync(); // what window activation / the watcher would do
            await Assert.That(town.Document.Text).Contains("Changed outside.");
            await Assert.That(forest.HasInfoBar).IsTrue();
            await Assert.That(forest.Document.Text).Contains("// local edit");

            forest.ReloadFromDiskCommand.Execute(null);
            await Assert.That(forest.Document.Text).Contains("Outside edit.");
            await Assert.That(forest.IsDirty).IsFalse();
            await Assert.That(forest.HasInfoBar).IsFalse();
        });

    [Test]
    public async Task Own_saves_are_not_treated_as_external_changes() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);
            var doc = app.Shell.ActiveDocument!;

            doc.Document.Insert(0, "// hi\n");
            await app.Shell.SaveCommand.ExecuteAsync(null);
            doc.Document.Insert(0, "// more\n");
            await app.Project.RescanAllAsync();

            await Assert.That(doc.HasInfoBar).IsFalse();
            await Assert.That(doc.Document.Text).StartsWith("// more\n// hi\n");
        });

    [Test]
    public async Task Auto_save_after_delay_writes_the_file() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { AutoSave = AutoSaveMode.AfterDelay, AutoSaveDelayMs = 200 }));
            await app.OpenAsync(main);
            var doc = app.Shell.ActiveDocument!;

            doc.Document.Insert(0, "// autosaved\n");
            await Assert.That(doc.IsDirty).IsTrue();

            await Assert.That(await UiTestHelpers.WaitUntil(() => !doc.IsDirty, 5000)).IsTrue();
            await Assert.That(File.ReadAllText(main)).StartsWith("// autosaved");
        });

    [Test]
    public async Task Auto_save_failure_on_a_read_only_folder_is_reported() =>
        await HeadlessSession.Run(async () =>
        {
            if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            {
                return; // read-only folders are a Unix permission; root ignores them
            }

            var main = UiTestHelpers.CopySample("multi-file", out var root);
            var chapters = Path.Join(root, "chapters");
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { AutoSave = AutoSaveMode.AfterDelay, AutoSaveDelayMs = 200 }));
            await app.OpenAsync(main);
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);

            File.SetUnixFileMode(chapters, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            try
            {
                forest.Document.Insert(0, "// can't save\n");
                await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.StatusText.StartsWith("Auto-save failed", StringComparison.Ordinal), 5000)).IsTrue();
                await Assert.That(forest.IsDirty).IsTrue();
            }
            finally
            {
                File.SetUnixFileMode(chapters, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        });

    [Test]
    public async Task Auto_save_on_focus_lost_skips_conflicted_files() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { AutoSave = AutoSaveMode.OnFocusLost }));
            await app.OpenAsync(main);
            var doc = app.Shell.ActiveDocument!;
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);

            forest.Document.Insert(0, "// mine\n");
            File.WriteAllText(Path.Join(root, "chapters", "forest.ink"), "=== forest ===\nTheirs\n-> END\n");
            await app.Project.RescanAllAsync();
            doc.Document.Insert(0, "// main edit\n");

            await app.Shell.OnWindowDeactivatedAsync();

            await Assert.That(doc.IsDirty).IsFalse();
            await Assert.That(File.ReadAllText(main)).StartsWith("// main edit");
            await Assert.That(forest.IsDirty).IsTrue();
            await Assert.That(File.ReadAllText(Path.Join(root, "chapters", "forest.ink"))).Contains("Theirs");
        });

    [Test]
    public async Task Closing_a_tab_with_auto_save_saves_it() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { AutoSave = AutoSaveMode.OnFocusLost }));
            await app.OpenAsync(main);
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);
            forest.Document.Insert(0, "// closing saves\n");

            app.Shell.CloseDocumentCommand.Execute(null);

            await Assert.That(await UiTestHelpers.WaitUntil(() => !forest.IsDirty, 5000)).IsTrue();
            await Assert.That(app.Dialogs.UnsavedPrompts).IsEqualTo(0);
            await Assert.That(File.ReadAllText(Path.Join(root, "chapters", "forest.ink"))).StartsWith("// closing saves");
            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument != forest, 5000)).IsTrue();
        });

    [Test]
    public async Task Closing_a_conflicted_tab_with_auto_save_asks_instead_of_overwriting() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with { AutoSave = AutoSaveMode.OnFocusLost }));
            await app.OpenAsync(main);
            var forestPath = Path.Join(root, "chapters", "forest.ink");
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);

            forest.Document.Insert(0, "// mine\n");
            File.WriteAllText(forestPath, "=== forest ===\nTheirs\n-> END\n");
            await app.Project.RescanAllAsync();
            forest.KeepMineCommand.Execute(null);

            app.Dialogs.UnsavedChoice = Services.UnsavedChangesChoice.Cancel;
            app.Shell.CloseDocumentCommand.Execute(null);

            await Assert.That(await UiTestHelpers.WaitUntil(() => app.Dialogs.UnsavedPrompts == 1, 5000)).IsTrue();
            await Assert.That(File.ReadAllText(forestPath)).Contains("Theirs");
            await Assert.That(forest.IsDirty).IsTrue();
            await Assert.That(app.Shell.ActiveDocument).IsEqualTo(forest);
        });

    [Test]
    public async Task Closing_asks_about_unsaved_files_that_are_no_longer_included() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);
            forest.Document.Insert(0, "// unsaved forest\n");
            await app.Shell.SaveCommand.ExecuteAsync(null); // nothing else dirty...
            forest.Document.Insert(0, "// more\n");

            // ...then drop forest's INCLUDE from main and save main.
            var mainDoc = app.Shell.OpenDocument(app.Project.Project.Main);
            await app.WaitForCompileAfter(() => mainDoc.Document.Text = mainDoc.Document.Text.Replace("INCLUDE chapters/forest.ink", "", StringComparison.Ordinal));
            await app.Project.SaveAsync(app.Project.Project.Main);
            await Assert.That(app.Project.Project.Find("chapters/forest.ink")).IsNull();

            app.Dialogs.UnsavedChoice = Services.UnsavedChangesChoice.Cancel;
            await Assert.That(await app.Shell.OnClosingAsync()).IsFalse();

            app.Dialogs.UnsavedChoice = Services.UnsavedChangesChoice.Save;
            await Assert.That(await app.Shell.OnClosingAsync()).IsTrue();
            await Assert.That(File.ReadAllText(Path.Join(root, "chapters", "forest.ink"))).StartsWith("// more\n// unsaved forest");
        });

    [Test]
    public async Task Closing_with_unsaved_changes_asks_and_can_cancel() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            await using var app = AppFixture.Create();
            await app.OpenAsync(main);
            app.Shell.ActiveDocument!.Document.Insert(0, "// unsaved\n");

            app.Dialogs.UnsavedChoice = Services.UnsavedChangesChoice.Cancel;
            await Assert.That(await app.Shell.OnClosingAsync()).IsFalse();
            await Assert.That(app.Shell.Project).IsNotNull();

            app.Dialogs.UnsavedChoice = Services.UnsavedChangesChoice.Save;
            await Assert.That(await app.Shell.OnClosingAsync()).IsTrue();
            await Assert.That(File.ReadAllText(main)).StartsWith("// unsaved");
        });
}
