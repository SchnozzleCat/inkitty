using Inkitty.Core.Diagnostics;

namespace Inkitty.Core.Tests.Diagnostics;

public class IssueMessageParserTests
{
    private static string? Resolve(string? file) => file ?? "main.ink";

    [Test]
    [Arguments("ERROR: 'chapters/forest.ink' line 12: Divert target not found: '-> foresst'", Ink.ErrorType.Error, IssueSeverity.Error, "chapters/forest.ink", 12, "Divert target not found: '-> foresst'")]
    [Arguments("WARNING: line 3: Apparent loose end", Ink.ErrorType.Warning, IssueSeverity.Warning, "main.ink", 3, "Apparent loose end")]
    [Arguments("TODO: 'main.ink' line 40: rewrite this bit", Ink.ErrorType.Author, IssueSeverity.Todo, "main.ink", 40, "rewrite this bit")]
    [Arguments("RUNTIME ERROR: 'main.ink' line 7: ran out of content", Ink.ErrorType.Error, IssueSeverity.RuntimeError, "main.ink", 7, "ran out of content")]
    [Arguments("RUNTIME WARNING: 'a.ink' line 2: something", Ink.ErrorType.Warning, IssueSeverity.RuntimeWarning, "a.ink", 2, "something")]
    public async Task Parses_known_formats(string message, Ink.ErrorType type, IssueSeverity severity, string file, int line, string text)
    {
        var issue = IssueMessageParser.Parse(message, type, Resolve);

        await Assert.That(issue.Severity).IsEqualTo(severity);
        await Assert.That(issue.File).IsEqualTo(file);
        await Assert.That(issue.Line).IsEqualTo(line);
        await Assert.That(issue.Message).IsEqualTo(text);
    }

    [Test]
    public async Task Message_without_location_has_no_range()
    {
        var issue = IssueMessageParser.Parse("ERROR: Something global went wrong", Ink.ErrorType.Error, Resolve);

        await Assert.That(issue.Range).IsNull();
        await Assert.That(issue.Message).IsEqualTo("Something global went wrong");
    }

    [Test]
    public async Task Runtime_message_with_path_only()
    {
        var issue = IssueMessageParser.Parse("RUNTIME ERROR: (knot.0.c-1): bad thing", Ink.ErrorType.Error, Resolve, isRuntime: true);

        await Assert.That(issue.Severity).IsEqualTo(IssueSeverity.RuntimeError);
        await Assert.That(issue.Range).IsNull();
        await Assert.That(issue.Message).IsEqualTo("bad thing");
    }

    [Test]
    public async Task Unparseable_message_is_kept_verbatim()
    {
        var issue = IssueMessageParser.Parse("weird", Ink.ErrorType.Warning, Resolve);

        await Assert.That(issue.Severity).IsEqualTo(IssueSeverity.Warning);
        await Assert.That(issue.Message).IsEqualTo("weird");
    }
}
