using Inkitty.Core.Projects;

namespace Inkitty.Core.Tests.Projects;

public class IncludeScannerTests
{
    [Test]
    public async Task Finds_include_lines_with_line_numbers()
    {
        var includes = IncludeScanner.Scan("Hello\nINCLUDE a.ink\n  INCLUDE   sub/b.ink  \n");

        await Assert.That(includes).IsEquivalentTo([new IncludeDirective(2, "a.ink"), new IncludeDirective(3, "sub/b.ink")]);
    }

    [Test]
    [Arguments("// INCLUDE a.ink")]
    [Arguments("/* INCLUDE a.ink */")]
    [Arguments("Some text INCLUDE a.ink")]
    [Arguments("INCLUDEa.ink")]
    [Arguments("INCLUDE")]
    public async Task Ignores_non_directives(string line)
    {
        await Assert.That(IncludeScanner.Scan(line)).IsEmpty();
    }

    [Test]
    public async Task Ignores_includes_inside_multiline_block_comments()
    {
        var includes = IncludeScanner.Scan("/* start\nINCLUDE hidden.ink\nend */\nINCLUDE shown.ink");

        await Assert.That(includes.Select(i => i.Path)).IsEquivalentTo(["shown.ink"]);
    }

    [Test]
    public async Task Strips_trailing_comment()
    {
        var includes = IncludeScanner.Scan("INCLUDE a.ink // the a file");

        await Assert.That(includes.Single().Path).IsEqualTo("a.ink");
    }

    [Test]
    public async Task Handles_crlf()
    {
        var includes = IncludeScanner.Scan("INCLUDE a.ink\r\nINCLUDE b.ink\r\n");

        await Assert.That(includes.Select(i => i.Path)).IsEquivalentTo(["a.ink", "b.ink"]);
    }
}
