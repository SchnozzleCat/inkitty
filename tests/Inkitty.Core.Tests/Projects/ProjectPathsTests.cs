using Inkitty.Core.Projects;

namespace Inkitty.Core.Tests.Projects;

public class ProjectPathsTests
{
    private static readonly string Root = Path.Join(Path.GetTempPath(), "proj");

    [Test]
    [Arguments("a.ink", "a.ink")]
    [Arguments("./a.ink", "a.ink")]
    [Arguments("sub/b.ink", "sub/b.ink")]
    [Arguments(@"sub\b.ink", "sub/b.ink")]
    [Arguments("sub/../c.ink", "c.ink")]
    [Arguments("  spaced.ink ", "spaced.ink")]
    public async Task Normalizes_relative_paths(string input, string expected)
    {
        await Assert.That(ProjectPaths.Normalize(Root, input)).IsEqualTo(expected);
    }

    [Test]
    [Arguments("../outside.ink")]
    [Arguments("")]
    [Arguments(".")]
    public async Task Rejects_paths_outside_root(string input)
    {
        await Assert.That(ProjectPaths.Normalize(Root, input)).IsNull();
    }
}
