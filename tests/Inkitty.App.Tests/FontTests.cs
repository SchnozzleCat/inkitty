using Avalonia.Media;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.Core.Settings;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class FontTests
{
    [Test]
    public async Task Default_editor_font_resolves_to_bundled_jetbrains_mono() =>
        await HeadlessSession.Run(async () =>
        {
            var typeface = new Typeface(FontFamily.Parse(AppSettings.DefaultEditorFont));
            var found = FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphs);

            await Assert.That(found).IsTrue();
            await Assert.That(glyphs!.FamilyName).IsEqualTo("JetBrains Mono");
        });

    [Test]
    public async Task Editor_uses_jetbrains_mono() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var editor = app.EditorFor(app.Shell.ActiveDocument!)!;

            FontManager.Current.TryGetGlyphTypeface(new Typeface(editor.TextEditor.FontFamily), out var glyphs);
            await Assert.That(glyphs?.FamilyName).IsEqualTo("JetBrains Mono");
        });
}
