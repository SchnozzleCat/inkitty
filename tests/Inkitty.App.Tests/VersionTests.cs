using Avalonia.Controls;
using Avalonia.LogicalTree;
using Inkitty.App.Tests.Infrastructure;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class VersionTests
{
    [Test]
    [Arguments("0.1.0+3216abd09cc934147a4c4125b54858fc51c08081", "v0.1.0")]
    [Arguments("0.2.0-beta.1+3216abd09cc9", "v0.2.0-beta.1")]
    [Arguments("0.0.0-dev+3216abd09cc934147a4c4125b54858fc51c08081", "dev 3216abd")]
    [Arguments("0.0.0-dev", "dev")]
    [Arguments("0-unstable-20261126", "0-unstable-20261126")]
    [Arguments(null, "dev")]
    public async Task Version_is_shown_as_the_release_tag_or_dev_with_the_commit(string? informational, string expected) =>
        await Assert.That(AppVersion.Format(informational)).IsEqualTo(expected);

    [Test]
    public async Task Status_bar_shows_the_version_bottom_left() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();

            var version = app.Window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Name == "VersionText");
            await Assert.That(version.Text).IsEqualTo(AppVersion.Display);
            await Assert.That(Grid.GetColumn(version)).IsEqualTo(0);
            await Assert.That(AppVersion.Display).StartsWith("dev"); // test builds aren't tagged
        });
}
