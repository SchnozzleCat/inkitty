using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.Views;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class SmokeTests
{
    [Test]
    public async Task Main_window_can_be_shown()
    {
        var title = await HeadlessSession.Run(() =>
        {
            var window = new MainWindow();
            window.Show();
            var t = window.Title;
            window.Close();
            return t;
        });

        await Assert.That(title).IsEqualTo("Inkitty");
    }
}
