using System.Text.RegularExpressions;

namespace Inkitty.Core.Symbols;

/// <summary>
/// Regex-based symbol scanning, used while the compiler's index is stale (the user has typed since
/// the last compile) or unavailable. Approximates ink's scoping: local stitches/labels first, then globals.
/// </summary>
public static partial class FallbackSymbolScanner
{
    [GeneratedRegex(@"^\s*={2,}\s*(?<func>function\s+)?(?<name>\w+)")]
    private static partial Regex KnotPattern();

    [GeneratedRegex(@"^\s*=\s*(?<name>\w+)")]
    private static partial Regex StitchPattern();

    [GeneratedRegex(@"^\s*(?:[*+]\s*|-\s*(?!>))+\((?<name>\w+)\)")]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"^\s*(?<kind>VAR|CONST|LIST)\s+(?<name>\w+)")]
    private static partial Regex DeclarationPattern();

    [GeneratedRegex(@"^\s*EXTERNAL\s+(?<name>\w+)")]
    private static partial Regex ExternalPattern();

    [GeneratedRegex(@"[\w.]")]
    private static partial Regex IdentifierChar();

    /// <summary>Scans all given files for definitions.</summary>
    public static IReadOnlyList<SymbolDefinition> ScanDefinitions(IEnumerable<(string File, string Text)> files)
    {
        List<SymbolDefinition> result = [];
        foreach (var (file, text) in files)
        {
            ScanFile(file, text, result);
        }

        return result;
    }

    /// <summary>
    /// Resolves the identifier path under <paramref name="position"/> using the given definitions.
    /// Returns the reference and its target, or null if there's no identifier there.
    /// </summary>
    public static SymbolReference? ResolveAt(string file, string text, SourcePosition position, IReadOnlyList<SymbolDefinition> definitions)
    {
        var lines = text.Split('\n');
        if (position.Line < 1 || position.Line > lines.Length)
        {
            return null;
        }

        var line = lines[position.Line - 1];
        var col = position.Column - 1;
        if (col < 0 || col >= line.Length || !IdentifierChar().IsMatch(line[col].ToString()))
        {
            return null;
        }

        // Expand to the whole dotted path, then to the component under the cursor.
        int start = col, end = col;
        while (start > 0 && IdentifierChar().IsMatch(line[start - 1].ToString()))
        {
            start--;
        }

        while (end < line.Length && IdentifierChar().IsMatch(line[end].ToString()))
        {
            end++;
        }

        var path = line[start..end].Trim('.');
        if (path.Length == 0)
        {
            return null;
        }

        var components = path.Split('.');
        var offsetInPath = col - start;
        var componentIndex = path[..Math.Min(offsetInPath, path.Length)].Count(c => c == '.');
        var prefix = string.Join('.', components.Take(componentIndex + 1));

        var componentStart = start + components.Take(componentIndex).Sum(c => c.Length + 1);
        var range = new SourceRange(
            file,
            new SourcePosition(position.Line, componentStart + 1),
            new SourcePosition(position.Line, componentStart + components[componentIndex].Length + 1));

        var (knot, stitch) = EnclosingFlow(lines, position.Line);
        return new SymbolReference(components[componentIndex], range, Resolve(prefix, knot, stitch, definitions));
    }

    internal static SymbolDefinition? Resolve(string path, string? knot, string? stitch, IReadOnlyList<SymbolDefinition> definitions)
    {
        IEnumerable<string> candidates = path.Contains('.', StringComparison.Ordinal)
            ? [path, knot is null ? path : $"{knot}.{path}"]
            :
            [
                .. stitch is not null && knot is not null ? new[] { $"{knot}.{stitch}.{path}" } : [],
                .. knot is not null ? new[] { $"{knot}.{path}" } : [],
                path,
            ];

        foreach (var candidate in candidates)
        {
            if (definitions.FirstOrDefault(d => d.QualifiedName == candidate) is { } def)
            {
                return def;
            }
        }

        // Labels can be referenced by bare name from anywhere within their knot.
        return definitions.FirstOrDefault(d => d.Kind == SymbolKind.Label && d.Name == path && (knot is null || d.Container?.StartsWith(knot, StringComparison.Ordinal) == true))
            ?? definitions.FirstOrDefault(d => d.Name == path && d.Kind is SymbolKind.Knot or SymbolKind.Function or SymbolKind.Variable or SymbolKind.Constant or SymbolKind.List or SymbolKind.External);
    }

    private static (string? Knot, string? Stitch) EnclosingFlow(string[] lines, int lineNumber)
    {
        string? knot = null, stitch = null;
        for (var i = 0; i < lineNumber && i < lines.Length; i++)
        {
            if (KnotPattern().Match(lines[i]) is { Success: true } k)
            {
                knot = k.Groups["name"].Value;
                stitch = null;
            }
            else if (StitchPattern().Match(lines[i]) is { Success: true } s)
            {
                stitch = s.Groups["name"].Value;
            }
        }

        return (knot, stitch);
    }

    private static void ScanFile(string file, string text, List<SymbolDefinition> result)
    {
        string? knot = null, stitch = null;
        var lineNumber = 0;
        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            if (KnotPattern().Match(line) is { Success: true } knotMatch)
            {
                var name = knotMatch.Groups["name"];
                knot = name.Value;
                stitch = null;
                var kind = knotMatch.Groups["func"].Success ? SymbolKind.Function : SymbolKind.Knot;
                result.Add(new SymbolDefinition(knot, knot, kind, RangeOf(file, lineNumber, name)));
            }
            else if (StitchPattern().Match(line) is { Success: true } stitchMatch)
            {
                var name = stitchMatch.Groups["name"];
                stitch = name.Value;
                var qualified = knot is null ? stitch : $"{knot}.{stitch}";
                result.Add(new SymbolDefinition(stitch, qualified, SymbolKind.Stitch, RangeOf(file, lineNumber, name), knot));
            }
            else if (DeclarationPattern().Match(line) is { Success: true } declMatch)
            {
                var name = declMatch.Groups["name"];
                var kind = declMatch.Groups["kind"].Value switch
                {
                    "CONST" => SymbolKind.Constant,
                    "LIST" => SymbolKind.List,
                    _ => SymbolKind.Variable,
                };
                result.Add(new SymbolDefinition(name.Value, name.Value, kind, RangeOf(file, lineNumber, name)));
            }
            else if (ExternalPattern().Match(line) is { Success: true } externalMatch)
            {
                var name = externalMatch.Groups["name"];
                result.Add(new SymbolDefinition(name.Value, name.Value, SymbolKind.External, RangeOf(file, lineNumber, name)));
            }

            if (LabelPattern().Match(line) is { Success: true } labelMatch)
            {
                var name = labelMatch.Groups["name"];
                var container = (knot, stitch) switch
                {
                    (null, _) => null,
                    (_, null) => knot,
                    _ => $"{knot}.{stitch}",
                };
                var qualified = container is null ? name.Value : $"{container}.{name.Value}";
                result.Add(new SymbolDefinition(name.Value, qualified, SymbolKind.Label, RangeOf(file, lineNumber, name), container));
            }
        }
    }

    private static SourceRange RangeOf(string file, int line, Group group) =>
        new(file, new SourcePosition(line, group.Index + 1), new SourcePosition(line, group.Index + group.Length + 1));
}
