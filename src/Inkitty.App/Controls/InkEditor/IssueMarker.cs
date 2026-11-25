using AvaloniaEdit.Document;
using Inkitty.Core.Diagnostics;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>An issue anchored in the document; moves with edits until the next compile replaces it.</summary>
public sealed class IssueMarker(Issue issue) : TextSegment
{
    public Issue Issue { get; } = issue;
}

/// <summary>Converts issues to document segments.</summary>
internal static class IssueMarkerFactory
{
    public static IEnumerable<IssueMarker> Create(TextDocument document, IEnumerable<Issue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Range is not { } range || range.Line < 1 || range.Line > document.LineCount)
            {
                continue;
            }

            var line = document.GetLineByNumber(range.Line);
            int start, end;
            if (range.IsWholeLine || range.Start.Line != range.End.Line && range.End.Line > document.LineCount)
            {
                // Whole trimmed line.
                var text = document.GetText(line.Offset, line.Length);
                var leading = text.Length - text.TrimStart().Length;
                var trailing = text.Length - text.TrimEnd().Length;
                start = line.Offset + leading;
                end = Math.Max(start, line.EndOffset - trailing);
            }
            else
            {
                start = line.Offset + Math.Clamp(range.Start.Column - 1, 0, line.Length);
                var endLine = document.GetLineByNumber(Math.Clamp(range.End.Line, range.Line, document.LineCount));
                end = endLine.Offset + Math.Clamp(range.End.Column - 1, 0, endLine.Length);
            }

            if (end <= start)
            {
                // Empty range (e.g. error at end of line): mark one character so it's visible.
                end = Math.Min(start + 1, document.TextLength);
                if (end <= start && start > 0)
                {
                    start--;
                }
            }

            yield return new IssueMarker(issue) { StartOffset = start, Length = Math.Max(0, end - start) };
        }
    }
}
