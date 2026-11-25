namespace Inkitty.Core.Projects;

/// <summary>An <c>INCLUDE</c> line found in a file.</summary>
/// <param name="Line">1-based line number.</param>
/// <param name="Path">The include path exactly as written (trimmed).</param>
public readonly record struct IncludeDirective(int Line, string Path);

/// <summary>
/// Cheap, comment-aware scan for <c>INCLUDE</c> lines. Mirrors the ink parser:
/// the directive must be the first thing on its line; <c>//</c> and <c>/* */</c> comments are skipped.
/// </summary>
public static class IncludeScanner
{
    private const string Keyword = "INCLUDE";

    public static IReadOnlyList<IncludeDirective> Scan(string text)
    {
        List<IncludeDirective> result = [];
        var inBlockComment = false;
        var lineNumber = 0;

        foreach (var rawLine in text.AsSpan().EnumerateLines())
        {
            lineNumber++;
            var line = StripComments(rawLine, ref inBlockComment).Trim();

            if (!line.StartsWith(Keyword, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[Keyword.Length..];
            if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
            {
                continue;
            }

            var path = rest.Trim();
            if (path.Length > 0)
            {
                result.Add(new IncludeDirective(lineNumber, path.ToString()));
            }
        }

        return result;
    }

    /// <summary>Removes comment text from one line, tracking block comments across lines.</summary>
    private static ReadOnlySpan<char> StripComments(ReadOnlySpan<char> line, ref bool inBlockComment)
    {
        if (!inBlockComment && line.IndexOf('/') < 0)
        {
            return line;
        }

        Span<char> buffer = line.Length <= 512 ? stackalloc char[line.Length] : new char[line.Length];
        var length = 0;

        for (var i = 0; i < line.Length; i++)
        {
            if (inBlockComment)
            {
                if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (line[i] == '/' && i + 1 < line.Length)
            {
                if (line[i + 1] == '/')
                {
                    break;
                }

                if (line[i + 1] == '*')
                {
                    inBlockComment = true;
                    i++;
                    continue;
                }
            }

            buffer[length++] = line[i];
        }

        return buffer[..length].ToArray();
    }
}
