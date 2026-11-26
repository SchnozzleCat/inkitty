using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Inkitty.App.Controls;
using Inkitty.App.Tests.Infrastructure;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class ToolTipTests
{
    /// <summary>Raises the tooltip-opening event the way hovering would, and reports whether it was allowed.</summary>
    private static bool WouldOpen(Control control)
    {
        var args = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent);
        control.RaiseEvent(args);
        return !args.Cancel;
    }

    private static TextBlock? MessageBlock(AppFixture app, string startsWith) =>
        app.Window.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => TrimmedToolTip.GetIsEnabled(t) && t.Text?.StartsWith(startsWith, StringComparison.Ordinal) == true);

    [Test]
    public async Task Issue_message_only_has_a_tooltip_when_it_is_cut_off() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var main = app.Shell.ActiveDocument!;
            var longTodo = "TODO: " + string.Join(' ', Enumerable.Repeat("a very long note that will not fit in the pane", 8));
            await app.WaitForCompileAfter(() => main.Document.Insert(main.Document.TextLength, "\n" + longTodo + "\n"));
            await UiTestHelpers.WaitUntil(() => MessageBlock(app, "a very long") is not null);

            var shortMessage = MessageBlock(app, "write a better opening line")!;
            var longMessage = MessageBlock(app, "a very long")!;

            await Assert.That(TrimmedToolTip.IsTrimmed(shortMessage)).IsFalse();
            await Assert.That(WouldOpen(shortMessage)).IsFalse();
            await Assert.That(TrimmedToolTip.IsTrimmed(longMessage)).IsTrue();
            await Assert.That(WouldOpen(longMessage)).IsTrue();
            await Assert.That(ToolTip.GetTip(longMessage)).IsEqualTo(longMessage.Text);
        });
}
