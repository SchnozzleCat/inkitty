using System.Text.RegularExpressions;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.Outline;

/// <summary>
/// Builds the outline of one ink file from its text: declarations, knots and functions, stitches inside
/// knots, and choices and labelled gathers nested by weave depth. Works line by line, so it runs on
/// every keystroke and on files that don't compile.
/// </summary>
public static partial class OutlineScanner
{
    private const int MaxNameLength = 60;

    [GeneratedRegex(@"^(?<bars>={2,})\s*(?<func>function\b\s*)?(?<name>[^\W\d]\w*)?\s*(?:\((?<params>[^)]*)\))?")]
    private static partial Regex KnotPattern();

    [GeneratedRegex(@"^=(?!=)\s*(?<name>[^\W\d]\w*)\s*(?:\((?<params>[^)]*)\))?")]
    private static partial Regex StitchPattern();

    [GeneratedRegex(@"^(?<bullets>(?:[*+]\s*)+)")]
    private static partial Regex ChoicePattern();

    [GeneratedRegex(@"^(?<bullets>(?:-(?!>)\s*)+)")]
    private static partial Regex GatherPattern();

    [GeneratedRegex(@"^\(\s*(?<label>[^\W\d]\w*)\s*\)\s*")]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"^(?<kind>VAR|CONST)\s+(?<name>[^\W\d]\w*)\s*(?:=\s*(?<value>.*))?$")]
    private static partial Regex VariablePattern();

    [GeneratedRegex(@"^LIST\s+(?<name>[^\W\d]\w*)\s*(?:=\s*(?<value>.*))?$")]
    private static partial Regex ListPattern();

    [GeneratedRegex(@"^EXTERNAL\s+(?<name>[^\W\d]\w*)\s*(?:\((?<params>[^)]*)\))?")]
    private static partial Regex ExternalPattern();

    [GeneratedRegex(@"^INCLUDE\s+(?<path>.+?)\s*$")]
    private static partial Regex IncludePattern();

    public static IReadOnlyList<OutlineItem> Scan(string file, string text)
    {
        var builder = new Builder(file);
        var lines = text.Split('\n');
        var inBlockComment = false;
        var blockDepth = 0; // multi-line { ... } blocks: their "- cond:" branches aren't gathers

        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i].TrimEnd('\r');
            var code = StripComments(raw, ref inBlockComment);
            var trimmed = code.TrimStart();
            var indent = code.Length - trimmed.Length;
            var lineNumber = i + 1;
            if (trimmed.Length == 0)
            {
                continue;
            }

            var depthBefore = blockDepth;
            blockDepth = Math.Max(0, blockDepth + BraceBalance(trimmed));

            if (KnotPattern().Match(trimmed) is { Success: true } knot)
            {
                blockDepth = 0;
                var name = knot.Groups["name"];
                if (name.Success)
                {
                    builder.Flow(
                        knot.Groups["func"].Success ? OutlineKind.Function : OutlineKind.Knot,
                        name.Value,
                        Parameters(knot),
                        Range(lineNumber, indent + name.Index, name.Length));
                }

                continue;
            }

            if (StitchPattern().Match(trimmed) is { Success: true } stitch)
            {
                blockDepth = 0;
                var name = stitch.Groups["name"];
                builder.Flow(OutlineKind.Stitch, name.Value, Parameters(stitch), Range(lineNumber, indent + name.Index, name.Length));
                continue;
            }

            if (depthBefore == 0 && ChoicePattern().Match(trimmed) is { Success: true } choice)
            {
                var bullets = choice.Groups["bullets"].Value;
                var rest = trimmed[choice.Length..];
                var label = LabelPattern().Match(rest);
                if (label.Success)
                {
                    rest = rest[label.Length..];
                }

                var (conditions, content) = SplitConditions(rest);
                var choiceText = ChoiceText(content);
                builder.Weave(
                    bullets.Contains('+', StringComparison.Ordinal) ? OutlineKind.StickyChoice : OutlineKind.Choice,
                    bullets.Count(c => c is '*' or '+'),
                    choiceText.Length > 0 ? choiceText : "(fallback)",
                    null,
                    SourceRange.WholeLine(string.Empty, lineNumber),
                    conditions.Count > 0 ? string.Join(' ', conditions) : null,
                    label.Success ? $"({label.Groups["label"].Value})" : null);
                continue;
            }

            if (depthBefore == 0 && GatherPattern().Match(trimmed) is { Success: true } gather)
            {
                var depth = gather.Groups["bullets"].Value.Count(c => c == '-');
                var label = LabelPattern().Match(trimmed[gather.Length..]);
                if (label.Success)
                {
                    var name = label.Groups["label"];
                    builder.Weave(OutlineKind.Gather, depth, name.Value, null, Range(lineNumber, indent + gather.Length + name.Index, name.Length));
                }
                else
                {
                    builder.CloseWeave(depth); // an unlabelled gather still ends the choices above it
                }

                continue;
            }

            if (VariablePattern().Match(trimmed) is { Success: true } variable)
            {
                var name = variable.Groups["name"];
                builder.Declaration(
                    variable.Groups["kind"].Value == "CONST" ? OutlineKind.Constant : OutlineKind.Variable,
                    name.Value,
                    variable.Groups["value"].Success ? Truncate(variable.Groups["value"].Value.Trim()) : null,
                    Range(lineNumber, indent + name.Index, name.Length));
            }
            else if (ListPattern().Match(trimmed) is { Success: true } list)
            {
                var name = list.Groups["name"];
                builder.Declaration(OutlineKind.List, name.Value, list.Groups["value"].Success ? Truncate(list.Groups["value"].Value.Trim()) : null, Range(lineNumber, indent + name.Index, name.Length));
            }
            else if (ExternalPattern().Match(trimmed) is { Success: true } external)
            {
                var name = external.Groups["name"];
                builder.Declaration(OutlineKind.External, name.Value, Parameters(external), Range(lineNumber, indent + name.Index, name.Length));
            }
            else if (IncludePattern().Match(trimmed) is { Success: true } include)
            {
                var path = include.Groups["path"];
                builder.Declaration(OutlineKind.Include, path.Value, null, Range(lineNumber, indent + path.Index, path.Length));
            }
        }

        return builder.Build();
    }

    /// <summary>The deepest item whose line is at or above <paramref name="line"/> (the element the caret is in).</summary>
    public static OutlineItem? ItemAt(IReadOnlyList<OutlineItem> items, int line)
    {
        OutlineItem? found = null;
        foreach (var item in Flatten(items))
        {
            if (item.Line > line)
            {
                break;
            }

            found = item;
        }

        return found;
    }

    /// <summary>All items in document order.</summary>
    public static IEnumerable<OutlineItem> Flatten(IEnumerable<OutlineItem> items) =>
        items.SelectMany(i => (IEnumerable<OutlineItem>)[i, .. Flatten(i.Children)]);

    /// <summary>A range on one line; <paramref name="index0"/> is a 0-based column. The builder fills in the file.</summary>
    private static SourceRange Range(int line, int index0, int length) =>
        new(string.Empty, new SourcePosition(line, index0 + 1), new SourcePosition(line, index0 + 1 + length));

    private static string? Parameters(Match match) =>
        match.Groups["params"] is { Success: true } p ? $"({string.Join(", ", p.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))})" : null;

    private static string Truncate(string text)
    {
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > MaxNameLength ? text[..(MaxNameLength - 1)].TrimEnd() + "…" : text;
    }

    /// <summary>Leading <c>{conditions}</c> of a choice, and what follows them.</summary>
    private static (List<string> Conditions, string Remainder) SplitConditions(string text)
    {
        List<string> conditions = [];
        var rest = text.TrimStart();
        while (rest.StartsWith('{'))
        {
            var close = MatchingBrace(rest);
            if (close < 0)
            {
                break;
            }

            conditions.Add("{" + Truncate(rest[1..close].Trim()) + "}");
            rest = rest[(close + 1)..].TrimStart();
        }

        return (conditions, rest);
    }

    /// <summary>What the player sees for a choice: text before and inside [ ], without diverts, tags or logic.</summary>
    private static string ChoiceText(string content)
    {
        var text = content;
        var divert = IndexOutsideBraces(text, "->");
        if (divert >= 0)
        {
            text = text[..divert];
        }

        var tag = IndexOutsideBraces(text, "#");
        if (tag >= 0)
        {
            text = text[..tag];
        }

        var open = text.IndexOf('[', StringComparison.Ordinal);
        if (open >= 0)
        {
            var close = text.IndexOf(']', open + 1);
            text = close >= 0 ? text[..open] + text[(open + 1)..close] : text[..open] + text[(open + 1)..];
        }

        return Truncate(text.Replace("<>", string.Empty, StringComparison.Ordinal).Trim());
    }

    private static int IndexOutsideBraces(string text, string needle)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\\':
                    i++;
                    continue;
                case '{':
                    depth++;
                    continue;
                case '}':
                    depth = Math.Max(0, depth - 1);
                    continue;
            }

            if (depth == 0 && string.CompareOrdinal(text, i, needle, 0, needle.Length) == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int MatchingBrace(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == '{')
            {
                depth++;
            }
            else if (text[i] == '}' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static int BraceBalance(string text)
    {
        var balance = 0;
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\\':
                    i++;
                    break;
                case '"':
                    inString = !inString;
                    break;
                case '{' when !inString:
                    balance++;
                    break;
                case '}' when !inString:
                    balance--;
                    break;
            }
        }

        return balance;
    }

    private static string StripComments(string line, ref bool inBlockComment)
    {
        var result = new System.Text.StringBuilder(line.Length);
        for (var i = 0; i < line.Length; i++)
        {
            if (inBlockComment)
            {
                if (line[i] == '*' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                result.Append(' '); // keep columns aligned
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
                    result.Append("  ");
                    i++;
                    continue;
                }
            }

            result.Append(line[i]);
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>Collects items into a tree: flows at the top, stitches in knots, weave by depth.</summary>
    private sealed class Builder(string file)
    {
        private sealed class Node(OutlineKind kind, string name, string? detail, SourceRange range, string? condition = null, string? label = null)
        {
            public string? Condition { get; } = condition;

            public string? Label { get; } = label;

            public OutlineKind Kind { get; } = kind;

            public string Name { get; } = name;

            public string? Detail { get; } = detail;

            public SourceRange Range { get; } = range;

            public List<Node> Children { get; } = [];
        }

        private readonly List<Node> _root = [];
        private readonly List<(int Depth, Node Node)> _weave = [];
        private Node? _knot;
        private Node? _stitch;

        public string File { get; } = file;

        public void Declaration(OutlineKind kind, string name, string? detail, SourceRange range) =>
            _root.Add(new Node(kind, name, detail, Fix(range)));

        public void Flow(OutlineKind kind, string name, string? detail, SourceRange range)
        {
            _weave.Clear();
            var node = new Node(kind, name, detail, Fix(range));
            if (kind == OutlineKind.Stitch && _knot is { Kind: OutlineKind.Knot } knot)
            {
                knot.Children.Add(node);
                _stitch = node;
            }
            else
            {
                _root.Add(node);
                _knot = kind == OutlineKind.Stitch ? null : node;
                _stitch = kind == OutlineKind.Stitch ? node : null;
            }
        }

        public void CloseWeave(int depth) => _weave.RemoveAll(w => w.Depth >= depth);

        public void Weave(OutlineKind kind, int depth, string name, string? detail, SourceRange range, string? condition = null, string? label = null)
        {
            CloseWeave(depth);
            var node = new Node(kind, name, detail, Fix(range), condition, label);
            var parent = _weave.Count > 0 ? _weave[^1].Node.Children : (_stitch ?? _knot)?.Children ?? _root;
            parent.Add(node);
            _weave.Add((depth, node));
        }

        public IReadOnlyList<OutlineItem> Build() => Convert(_root, string.Empty);

        private SourceRange Fix(SourceRange range) => range with { File = File };

        private static List<OutlineItem> Convert(List<Node> nodes, string parentKey)
        {
            List<OutlineItem> items = [];
            Dictionary<string, int> seen = [];
            foreach (var node in nodes)
            {
                var baseKey = $"{parentKey}/{node.Kind}:{node.Name}";
                var n = seen[baseKey] = seen.GetValueOrDefault(baseKey) + 1;
                var key = n == 1 ? baseKey : $"{baseKey}#{n}";
                items.Add(new OutlineItem(node.Kind, node.Name, node.Detail, node.Range, Convert(node.Children, key), node.Condition, node.Label) { Key = key });
            }

            return items;
        }
    }
}
