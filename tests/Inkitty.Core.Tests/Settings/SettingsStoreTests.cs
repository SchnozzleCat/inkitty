using Inkitty.Core.Settings;
using Inkitty.Core.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Inkitty.Core.Tests.Settings;

public class SettingsStoreTests
{
    [Test]
    public async Task Round_trips_settings()
    {
        using var dir = new TempDirectory();
        var store = new SettingsStore(dir.Path);

        store.Update(s => s with { AutoSave = AutoSaveMode.AfterDelay, EditorFontSize = 16, Theme = ThemePreference.Dark });
        await store.FlushAsync();

        var reloaded = new SettingsStore(dir.Path).Current;
        await Assert.That(reloaded.AutoSave).IsEqualTo(AutoSaveMode.AfterDelay);
        await Assert.That(reloaded.EditorFontSize).IsEqualTo(16);
        await Assert.That(reloaded.Theme).IsEqualTo(ThemePreference.Dark);
    }

    [Test]
    public async Task Saves_after_debounce()
    {
        using var dir = new TempDirectory();
        var time = new FakeTimeProvider();
        var store = new SettingsStore(dir.Path, time);

        store.Update(s => s with { WordWrap = false });
        await Assert.That(dir.Exists(SettingsStore.FileName)).IsFalse();

        time.Advance(TimeSpan.FromSeconds(1));
        for (var i = 0; i < 50 && !dir.Exists(SettingsStore.FileName); i++)
        {
            await Task.Yield();
            Thread.Sleep(10);
        }

        await Assert.That(dir.Exists(SettingsStore.FileName)).IsTrue();
    }

    [Test]
    public async Task Corrupt_file_falls_back_to_defaults_and_is_backed_up()
    {
        using var dir = new TempDirectory();
        dir.Write(SettingsStore.FileName, "{ not json");

        var store = new SettingsStore(dir.Path);

        await Assert.That(store.Current).IsEqualTo(new AppSettings());
        await Assert.That(dir.Exists(SettingsStore.FileName + ".bak")).IsTrue();
    }

    [Test]
    public async Task Old_default_editor_font_is_upgraded_but_custom_fonts_are_kept()
    {
        using var dir = new TempDirectory();
        dir.Write(SettingsStore.FileName, """{ "editorFontFamily": "Cascadia Mono, Menlo, Consolas, DejaVu Sans Mono, monospace" }""");
        await Assert.That(new SettingsStore(dir.Path).Current.EditorFontFamily).IsEqualTo(AppSettings.DefaultEditorFont);

        dir.Write(SettingsStore.FileName, """{ "editorFontFamily": "Iosevka" }""");
        await Assert.That(new SettingsStore(dir.Path).Current.EditorFontFamily).IsEqualTo("Iosevka");
    }

    [Test]
    public async Task Recent_projects_are_deduplicated_and_capped()
    {
        var settings = new AppSettings();
        for (var i = 0; i < 15; i++)
        {
            settings = settings.WithRecentProject($"/p/{i}.ink");
        }

        settings = settings.WithRecentProject("/p/3.ink");

        await Assert.That(settings.RecentProjects.Count).IsEqualTo(AppSettings.MaxRecentProjects);
        await Assert.That(settings.RecentProjects[0]).IsEqualTo("/p/3.ink");
        await Assert.That(settings.RecentProjects.Count(p => p == "/p/3.ink")).IsEqualTo(1);
    }

    [Test]
    public async Task Values_are_clamped()
    {
        var settings = new AppSettings { EditorFontSize = 1000, CompileDebounceMs = 0 };

        await Assert.That(settings.EditorFontSize).IsEqualTo(72);
        await Assert.That(settings.CompileDebounceMs).IsEqualTo(50);
    }

    [Test]
    public async Task Config_directory_honours_xdg_on_linux()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("Linux only");
        }

        var path = AppPaths.ConfigDirectory(name => name == "XDG_CONFIG_HOME" ? "/xdg" : null);

        await Assert.That(path).IsEqualTo("/xdg/Inkitty");
    }
}
