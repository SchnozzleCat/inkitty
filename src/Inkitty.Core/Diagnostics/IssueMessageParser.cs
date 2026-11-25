using System.Text.RegularExpressions;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.Diagnostics;

/// <summary>
/// Parses ink's error strings, e.g. <c>ERROR: 'chapters/forest.ink' line 12: Divert target not found</c>.
/// </summary>
public static partial class IssueMessageParser
{
    [GeneratedRegex(@"^(?<kind>RUNTIME ERROR|RUNTIME WARNING|ERROR|WARNING|TODO):\s*(?:'(?<file>[^']*)'\s*)?(?:line (?<line>\d+):)?(?:\s*\((?<path>[^)]*)\):)?\s*(?<message>.*)$", RegexOptions.Singleline)]
    private static partial Regex MessagePattern();

    /// <param name="message">The raw message passed to ink's error handler.</param>
    /// <param name="type">ink's classification of the message.</param>
    /// <param name="resolveFile">
    /// Maps the file name ink reports (null when absent) to a project-relative path,
    /// or null if it can't be resolved.
    /// </param>
    /// <param name="isRuntime">Whether this came from the runtime (story.onError) rather than the compiler.</param>
    public static Issue Parse(string message, Ink.ErrorType type, Func<string?, string?> resolveFile, bool isRuntime = false)
    {
        var match = MessagePattern().Match(message.Trim());
        if (!match.Success)
        {
            return new Issue(SeverityFrom(type, null, isRuntime), message.Trim(), null, isRuntime ? IssueOrigin.Runtime : IssueOrigin.Compiler);
        }

        var kind = match.Groups["kind"].Value;
        var severity = SeverityFrom(type, kind, isRuntime);
        var text = match.Groups["message"].Value.Trim();

        SourceRange? range = null;
        if (match.Groups["line"].Success && int.TryParse(match.Groups["line"].ValueSpan, out var line))
        {
            var reportedFile = match.Groups["file"].Success && match.Groups["file"].Length > 0 ? match.Groups["file"].Value : null;
            if (resolveFile(reportedFile) is { } file)
            {
                range = SourceRange.WholeLine(file, line);
            }
        }

        return new Issue(severity, text, range, severity.IsRuntime ? IssueOrigin.Runtime : IssueOrigin.Compiler);
    }

    private static IssueSeverity SeverityFrom(Ink.ErrorType type, string? kind, bool isRuntime) => kind switch
    {
        "RUNTIME ERROR" => IssueSeverity.RuntimeError,
        "RUNTIME WARNING" => IssueSeverity.RuntimeWarning,
        "TODO" => IssueSeverity.Todo,
        _ => (type, isRuntime) switch
        {
            (Ink.ErrorType.Author, _) => IssueSeverity.Todo,
            (Ink.ErrorType.Warning, true) => IssueSeverity.RuntimeWarning,
            (Ink.ErrorType.Warning, false) => IssueSeverity.Warning,
            (_, true) => IssueSeverity.RuntimeError,
            _ => IssueSeverity.Error,
        },
    };
}
