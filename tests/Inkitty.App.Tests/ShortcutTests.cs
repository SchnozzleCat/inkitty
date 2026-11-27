using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Inkitty.App.Input;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.ViewModels;
using Inkitty.App.Views;
using Inkitty.Core.Settings;

namespace Inkitty.App.Tests;

/// <summary>The shortcut catalog and parsing (no UI).</summary>
public class ShortcutCatalogTests
{
    [Test]
    public async Task Ids_are_unique_and_every_default_parses_on_both_platforms()
    {
        await Assert.That(ShortcutCatalog.Commands.Select(c => c.Id).Distinct().Count()).IsEqualTo(ShortcutCatalog.Commands.Count);
        foreach (var platform in new[] { ShortcutPlatform.Windows, ShortcutPlatform.Mac })
        {
            foreach (var command in ShortcutCatalog.Commands)
            {
                await Assert.That(ShortcutCatalog.TryParse(command.DefaultFor(platform), platform, out var gesture)).IsTrue();
                if (gesture is not null)
                {
                    await Assert.That(ShortcutCatalog.IsAllowed(gesture)).IsTrue();
                }
            }
        }
    }

    [Test]
    public async Task Defaults_dont_clash()
    {
        foreach (var platform in new[] { ShortcutPlatform.Windows, ShortcutPlatform.Mac })
        {
            var gestures = ShortcutCatalog.Resolve(new AppSettings(), platform).Values.OfType<KeyGesture>().ToList();
            await Assert.That(gestures.Distinct().Count()).IsEqualTo(gestures.Count);
        }
    }

    [Test]
    public async Task Mod_is_ctrl_or_cmd()
    {
        ShortcutCatalog.TryParse("Mod+Shift+S", ShortcutPlatform.Windows, out var windows);
        ShortcutCatalog.TryParse("Mod+Shift+S", ShortcutPlatform.Mac, out var mac);

        await Assert.That(windows).IsEqualTo(new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift));
        await Assert.That(mac).IsEqualTo(new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift));
        await Assert.That(ShortcutCatalog.ToStored(windows, ShortcutPlatform.Windows)).IsEqualTo("Mod+Shift+S");
        await Assert.That(ShortcutCatalog.ToStored(mac, ShortcutPlatform.Mac)).IsEqualTo("Mod+Shift+S");

        // Ctrl on a Mac is its own modifier, not Mod.
        var macCtrl = new KeyGesture(Key.OemMinus, KeyModifiers.Control);
        await Assert.That(ShortcutCatalog.ToStored(macCtrl, ShortcutPlatform.Mac)).IsEqualTo("Ctrl+OemMinus");
        ShortcutCatalog.TryParse("Ctrl+OemMinus", ShortcutPlatform.Mac, out var back);
        await Assert.That(back).IsEqualTo(macCtrl);
    }

    [Test]
    public async Task Stored_text_round_trips()
    {
        foreach (var gesture in new[]
        {
            new KeyGesture(Key.F12),
            new KeyGesture(Key.Left, KeyModifiers.Alt),
            new KeyGesture(Key.D1, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift),
            new KeyGesture(Key.OemComma, KeyModifiers.Control),
        })
        {
            var stored = ShortcutCatalog.ToStored(gesture, ShortcutPlatform.Windows);
            await Assert.That(ShortcutCatalog.TryParse(stored, ShortcutPlatform.Windows, out var parsed)).IsTrue();
            await Assert.That(parsed).IsEqualTo(gesture);
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    public async Task Empty_text_is_no_shortcut(string text)
    {
        await Assert.That(ShortcutCatalog.TryParse(text, ShortcutPlatform.Windows, out var gesture)).IsTrue();
        await Assert.That(gesture).IsNull();
    }

    [Test]
    public async Task Bad_stored_text_falls_back_to_the_default()
    {
        var settings = new AppSettings { KeyBindings = new Dictionary<string, string> { ["file.save"] = "Ctrl+Bogus", ["unknown.command"] = "Ctrl+Q" } };

        var resolved = ShortcutCatalog.Resolve(settings, ShortcutPlatform.Windows);

        await Assert.That(resolved["file.save"]).IsEqualTo(new KeyGesture(Key.S, KeyModifiers.Control));
        await Assert.That(resolved.ContainsKey("unknown.command")).IsFalse();
    }

    [Test]
    public async Task Typing_keys_cant_be_shortcuts()
    {
        await Assert.That(ShortcutCatalog.IsAllowed(new KeyGesture(Key.A))).IsFalse();
        await Assert.That(ShortcutCatalog.IsAllowed(new KeyGesture(Key.A, KeyModifiers.Shift))).IsFalse();
        await Assert.That(ShortcutCatalog.IsAllowed(new KeyGesture(Key.Enter))).IsFalse();
        await Assert.That(ShortcutCatalog.IsAllowed(new KeyGesture(Key.A, KeyModifiers.Control))).IsTrue();
        await Assert.That(ShortcutCatalog.IsAllowed(new KeyGesture(Key.A, KeyModifiers.Alt))).IsTrue();
        await Assert.That(ShortcutCatalog.IsAllowed(new KeyGesture(Key.F5))).IsTrue();
        await Assert.That(ShortcutCatalog.IsAllowed(new KeyGesture(Key.F5, KeyModifiers.Shift))).IsTrue();
    }

    [Test]
    public async Task Formats_for_each_platform()
    {
        var gesture = new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Shift);
        await Assert.That(ShortcutCatalog.Format(gesture, ShortcutPlatform.Windows)).IsEqualTo("Ctrl+Shift+S");
        await Assert.That(ShortcutCatalog.Format(new KeyGesture(Key.S, KeyModifiers.Meta | KeyModifiers.Shift), ShortcutPlatform.Mac)).IsEqualTo("⇧⌘S");
        await Assert.That(ShortcutCatalog.Format(new KeyGesture(Key.OemComma, KeyModifiers.Control), ShortcutPlatform.Windows)).IsEqualTo("Ctrl+,");
        await Assert.That(ShortcutCatalog.Format(new KeyGesture(Key.Left, KeyModifiers.Alt), ShortcutPlatform.Windows)).IsEqualTo("Alt+←");
        await Assert.That(ShortcutCatalog.Format(null, ShortcutPlatform.Windows)).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Browser_reserved_shortcuts_are_recognised()
    {
        var browser = ShortcutPlatform.Windows with { IsBrowser = true };
        await Assert.That(ShortcutCatalog.IsBrowserReserved(new KeyGesture(Key.W, KeyModifiers.Control), browser)).IsTrue();
        await Assert.That(ShortcutCatalog.IsBrowserReserved(new KeyGesture(Key.T, KeyModifiers.Control | KeyModifiers.Shift), browser)).IsTrue();
        await Assert.That(ShortcutCatalog.IsBrowserReserved(new KeyGesture(Key.S, KeyModifiers.Control), browser)).IsFalse();
        await Assert.That(ShortcutCatalog.IsBrowserReserved(new KeyGesture(Key.W, KeyModifiers.Control | KeyModifiers.Alt), browser)).IsFalse();
    }
}

/// <summary>Changing shortcuts: the service, the editor and the bindings in the running app.</summary>
[NotInParallel("ui")]
public class ShortcutTests
{
    private static ShortcutService NewService(out SettingsStore store)
    {
        store = new SettingsStore(Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N")));
        return new ShortcutService(store, ShortcutPlatform.Windows);
    }

    [Test]
    public async Task Setting_a_shortcut_stores_only_the_change()
    {
        var shortcuts = NewService(out var store);
        var changed = 0;
        shortcuts.Changed += (_, _) => changed++;

        shortcuts.Set("file.save", new KeyGesture(Key.S, KeyModifiers.Control | KeyModifiers.Alt));
        await Assert.That(store.Current.KeyBindings).IsEquivalentTo(new Dictionary<string, string> { ["file.save"] = "Mod+Alt+S" });
        await Assert.That(shortcuts.IsCustomised("file.save")).IsTrue();
        await Assert.That(shortcuts.DisplayText("file.save")).IsEqualTo("Ctrl+Alt+S");
        await Assert.That(changed).IsEqualTo(1);

        // Choosing the default again removes the override.
        shortcuts.Set("file.save", new KeyGesture(Key.S, KeyModifiers.Control));
        await Assert.That(store.Current.KeyBindings.Count).IsEqualTo(0);
        await Assert.That(shortcuts.IsCustomised("file.save")).IsFalse();

        shortcuts.Set("go.nextIssue", null);
        await Assert.That(store.Current.KeyBindings["go.nextIssue"]).IsEqualTo(string.Empty);
        await Assert.That(shortcuts.GestureFor("go.nextIssue")).IsNull();

        shortcuts.ResetAll();
        await Assert.That(store.Current.KeyBindings.Count).IsEqualTo(0);
        await Assert.That(shortcuts.GestureFor("go.nextIssue")).IsEqualTo(new KeyGesture(Key.F8));
    }

    [Test]
    public async Task Conflicts_are_reported_on_both_commands()
    {
        var shortcuts = NewService(out _);
        using var editor = new KeyboardShortcutsViewModel(shortcuts);

        shortcuts.Set("story.restart", new KeyGesture(Key.S, KeyModifiers.Control));

        var restart = editor.All.Single(r => r.Id == "story.restart");
        var save = editor.All.Single(r => r.Id == "file.save");
        await Assert.That(restart.Warning).IsEqualTo("Also the shortcut for Save");
        await Assert.That(save.Warning).IsEqualTo("Also the shortcut for Restart story");
        await Assert.That(editor.HasCustomisations).IsTrue();

        restart.ResetCommand.Execute(null);
        await Assert.That(restart.Warning).IsNull();
        await Assert.That(save.Warning).IsNull();
    }

    [Test]
    public async Task Recording_assigns_the_key_and_suspends_shortcuts_meanwhile()
    {
        var shortcuts = NewService(out _);
        using var editor = new KeyboardShortcutsViewModel(shortcuts);
        var row = editor.All.Single(r => r.Id == "story.map");

        row.RecordCommand.Execute(null);
        await Assert.That(row.IsRecording).IsTrue();
        await Assert.That(row.ButtonText).IsEqualTo("Press a shortcut…");
        await Assert.That(shortcuts.IsSuspended).IsTrue();

        // A lone modifier waits for the rest; a plain letter is refused with a hint.
        await Assert.That(editor.HandleKey(Key.LeftCtrl, KeyModifiers.Control)).IsFalse();
        await Assert.That(editor.HandleKey(Key.M, KeyModifiers.None)).IsTrue();
        await Assert.That(editor.RecordingHint).IsNotNull();
        await Assert.That(row.IsRecording).IsTrue();

        await Assert.That(editor.HandleKey(Key.M, KeyModifiers.Control | KeyModifiers.Alt)).IsTrue();
        await Assert.That(row.IsRecording).IsFalse();
        await Assert.That(row.GestureText).IsEqualTo("Ctrl+Alt+M");
        await Assert.That(shortcuts.IsSuspended).IsFalse();
    }

    [Test]
    public async Task Escape_cancels_recording_without_changing_the_shortcut()
    {
        var shortcuts = NewService(out var store);
        using var editor = new KeyboardShortcutsViewModel(shortcuts);
        var row = editor.All.Single(r => r.Id == "file.save");

        row.RecordCommand.Execute(null);
        editor.HandleKey(Key.Escape, KeyModifiers.None);

        await Assert.That(row.IsRecording).IsFalse();
        await Assert.That(shortcuts.IsSuspended).IsFalse();
        await Assert.That(store.Current.KeyBindings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Filter_matches_names_categories_and_shortcuts()
    {
        using var editor = new KeyboardShortcutsViewModel(NewService(out _));

        editor.Filter = "save";
        await Assert.That(editor.Rows.Select(r => r.Id)).IsEquivalentTo(["file.save", "file.saveAll"]);
        editor.Filter = "F8";
        await Assert.That(editor.Rows.Select(r => r.Id)).IsEquivalentTo(["go.nextIssue", "go.previousIssue"]);
        editor.Filter = "story map";
        await Assert.That(editor.Rows.Select(r => r.Id)).IsEquivalentTo(["story.map"]);
        editor.Filter = "";
        await Assert.That(editor.Rows.Count).IsEqualTo(ShortcutCatalog.Commands.Count);
    }

    [Test]
    public async Task A_changed_shortcut_runs_its_command_and_relabels_the_menu() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            var settingsOpened = 0;
            app.Shell.SettingsRequested += (_, _) => settingsOpened++;
            var item = app.Window.MainView.GetLogicalDescendants().OfType<MenuItem>().Single(m => Shortcut.GetId(m) == "app.settings");
            var command = app.Shell.Shortcuts.Platform.CommandModifier;
            await Assert.That(item.InputGesture).IsEqualTo(new KeyGesture(Key.OemComma, command));

            app.Shell.Shortcuts.Set("app.settings", new KeyGesture(Key.F9));
            await Assert.That(item.InputGesture).IsEqualTo(new KeyGesture(Key.F9));

            app.Window.KeyPress(Key.F9, RawInputModifiers.None, PhysicalKey.F9, null);
            await UiTestHelpers.WaitUntil(() => settingsOpened > 0, 2000);
            await Assert.That(settingsOpened).IsEqualTo(1);

            // The old shortcut no longer does anything.
            app.Window.KeyPress(Key.OemComma, command == KeyModifiers.Meta ? RawInputModifiers.Meta : RawInputModifiers.Control, PhysicalKey.Comma, null);
            await UiTestHelpers.WaitUntil(() => false, 200);
            await Assert.That(settingsOpened).IsEqualTo(1);
        });

    [Test]
    public async Task Shortcuts_editor_records_a_key_pressed_in_the_dialog() =>
        await HeadlessSession.Run(async () =>
        {
            MainView.UseDialogWindows = false;
            try
            {
                await using var app = AppFixture.Create();
                var view = app.Window.MainView;

                app.Shell.OpenKeyboardShortcutsCommand.Execute(null);
                await Assert.That(await UiTestHelpers.WaitUntil(() => view.GetVisualDescendants().OfType<KeyboardShortcutsView>().Any())).IsTrue();
                var editor = view.GetVisualDescendants().OfType<KeyboardShortcutsView>().Single();
                var vm = (KeyboardShortcutsViewModel)editor.DataContext!;
                var row = vm.All.Single(r => r.Id == "story.restart");

                row.RecordCommand.Execute(null);
                await UiTestHelpers.WaitUntil(() => app.Window.KeyBindings.Count == 0, 1000);
                await Assert.That(app.Window.KeyBindings.Count).IsEqualTo(0); // suspended while recording

                // The key goes to the recorder rather than running a command.
                editor.Focus();
                app.Window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
                await UiTestHelpers.WaitUntil(() => !row.IsRecording, 1000);
                await Assert.That(row.GestureText).IsEqualTo("F5");
                await Assert.That(app.Settings.Current.KeyBindings["story.restart"]).IsEqualTo("F5");
                await Assert.That(app.Window.KeyBindings.Any(b => Equals(b.Gesture, new KeyGesture(Key.F5)))).IsTrue();

                row.RecordCommand.Execute(null);
                app.Window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await UiTestHelpers.WaitUntil(() => !row.IsRecording, 1000);
                await Assert.That(view.IsModalOpen).IsTrue(); // Esc cancelled the recording only
                await Assert.That(row.GestureText).IsEqualTo("F5");

                view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Close").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Assert.That(await UiTestHelpers.WaitUntil(() => !view.IsModalOpen)).IsTrue();
            }
            finally
            {
                MainView.UseDialogWindows = true;
            }
        });

    [Test]
    public async Task Saved_shortcuts_apply_at_startup() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create(configure: s => s.Update(x => x with
            {
                KeyBindings = new Dictionary<string, string> { ["view.resetLayout"] = "Mod+Alt+L", ["file.save"] = "" },
            }));
            var command = app.Shell.Shortcuts.Platform.CommandModifier;

            await Assert.That(app.Window.KeyBindings.Any(b => Equals(b.Gesture, new KeyGesture(Key.L, command | KeyModifiers.Alt)) && b.Command == app.Shell.ResetLayoutCommand)).IsTrue();
            await Assert.That(app.Window.KeyBindings.Any(b => b.Command == app.Shell.SaveCommand)).IsFalse();
            var save = app.Window.MainView.GetLogicalDescendants().OfType<MenuItem>().Single(m => Shortcut.GetId(m) == "file.save");
            await Assert.That(save.InputGesture).IsNull();
        });
}
