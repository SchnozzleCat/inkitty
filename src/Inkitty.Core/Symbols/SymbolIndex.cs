using System.Collections.Frozen;
using Inkitty.Core.FileSystem;

namespace Inkitty.Core.Symbols;

/// <summary>
/// Definitions and references for a whole project, produced from one compilation (or one fallback scan).
/// Lookups by position are O(log n) per file.
/// </summary>
public sealed class SymbolIndex
{
    private readonly FrozenDictionary<string, SymbolReference[]> _referencesByFile;
    private readonly FrozenDictionary<string, SymbolDefinition[]> _definitionsByFile;

    public SymbolIndex(IEnumerable<SymbolDefinition> definitions, IEnumerable<SymbolReference> references)
    {
        Definitions = [.. definitions.DistinctBy(d => (d.QualifiedName, d.Kind, d.Range))];
        References = [.. references];
        _definitionsByFile = Definitions
            .GroupBy(d => d.Range.File, PathComparer.Default)
            .ToFrozenDictionary(g => g.Key, g => g.OrderBy(d => d.Range.Start).ToArray(), PathComparer.Default);
        _referencesByFile = References
            .GroupBy(r => r.Range.File, PathComparer.Default)
            .ToFrozenDictionary(g => g.Key, g => g.OrderBy(r => r.Range.Start).ToArray(), PathComparer.Default);
    }

    public static SymbolIndex Empty { get; } = new([], []);

    public IReadOnlyList<SymbolDefinition> Definitions { get; }

    public IReadOnlyList<SymbolReference> References { get; }

    public IReadOnlyList<SymbolDefinition> DefinitionsIn(string file) =>
        _definitionsByFile.GetValueOrDefault(file) ?? [];

    public IReadOnlyList<SymbolReference> ReferencesIn(string file) =>
        _referencesByFile.GetValueOrDefault(file) ?? [];

    /// <summary>Knots, stitches and functions of a file, in source order (for the outline).</summary>
    public IReadOnlyList<SymbolDefinition> OutlineOf(string file) =>
        [.. DefinitionsIn(file).Where(d => d.Kind is SymbolKind.Knot or SymbolKind.Stitch or SymbolKind.Function)];

    /// <summary>The reference whose token contains <paramref name="position"/>, if any.</summary>
    public SymbolReference? FindReferenceAt(string file, SourcePosition position) =>
        FindContaining(_referencesByFile.GetValueOrDefault(file), position, r => r.Range);

    /// <summary>The definition whose name token contains <paramref name="position"/>, if any.</summary>
    public SymbolDefinition? FindDefinitionAt(string file, SourcePosition position) =>
        FindContaining(_definitionsByFile.GetValueOrDefault(file), position, d => d.Range);

    public IEnumerable<SymbolDefinition> FindByName(string name) =>
        Definitions.Where(d => d.Name == name || d.QualifiedName == name);

    private static T? FindContaining<T>(T[]? items, SourcePosition position, Func<T, SourceRange> range)
        where T : class
    {
        if (items is null || items.Length == 0)
        {
            return null;
        }

        // Binary search for the last item starting at or before the position, then scan back a little
        // (ranges never nest deeply, so a short backwards scan is enough).
        int lo = 0, hi = items.Length - 1, last = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (range(items[mid]).Start <= position)
            {
                last = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        for (var i = last; i >= 0 && i > last - 8; i--)
        {
            var r = range(items[i]);
            if (!r.IsWholeLine && r.Contains(position))
            {
                return items[i];
            }
        }

        return null;
    }
}
