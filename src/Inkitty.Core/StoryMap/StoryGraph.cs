using System.Collections.Frozen;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.StoryMap;

public enum StoryNodeKind
{
    /// <summary>Top-level content before the first knot.</summary>
    Start,
    Knot,
    Stitch,

    /// <summary>A shared node for every <c>-> END</c> / <c>-> DONE</c>.</summary>
    End,
}

public enum StoryEdgeKind
{
    /// <summary>A plain <c>-> target</c>.</summary>
    Divert,

    /// <summary>A divert inside a choice; the edge is labelled with the choice text.</summary>
    Choice,

    /// <summary><c>-> target -></c></summary>
    Tunnel,

    /// <summary><c>&lt;- target</c></summary>
    Thread,

    /// <summary>A knot with no content of its own flows into its first stitch.</summary>
    Auto,
}

/// <summary>A knot or stitch (or the start/end of the story).</summary>
/// <param name="Id">Dotted ink path (<c>forest</c>, <c>forest.clearing</c>), or <see cref="StoryGraph.StartId"/>/<see cref="StoryGraph.EndId"/>.</param>
/// <param name="Range">The name in the source, for navigation.</param>
/// <param name="Parent">The knot of a stitch.</param>
public sealed record StoryNode(string Id, string Title, StoryNodeKind Kind, SourceRange? Range, string? Parent = null);

/// <summary>A way the story can move from one node to another.</summary>
/// <param name="Range">Where the divert (or choice) is written.</param>
/// <param name="Condition">
/// The condition guarding this way out, as written (e.g. <c>{has_torch}</c>, <c>{coins &gt; 3}</c>,
/// <c>{else}</c>); null if it's always taken or offered.
/// </param>
public sealed record StoryEdge(string From, string To, StoryEdgeKind Kind, string? Label = null, SourceRange? Range = null, string? Condition = null)
{
    public bool IsConditional => Condition is not null;
}

/// <summary>
/// The flow of a story at knot/stitch level: which knots can lead to which, and through which choices.
/// Built from the compiler's parsed hierarchy; variable diverts (<c>-> some_var</c>) can't be known statically and are left out.
/// </summary>
public sealed class StoryGraph
{
    public const string StartId = "(start)";
    public const string EndId = "(end)";

    private readonly FrozenDictionary<string, StoryNode> _byId;

    public StoryGraph(IEnumerable<StoryNode> nodes, IEnumerable<StoryEdge> edges)
    {
        Nodes = [.. nodes.DistinctBy(n => n.Id)];
        _byId = Nodes.ToFrozenDictionary(n => n.Id);
        Edges = [.. edges.Where(e => _byId.ContainsKey(e.From) && _byId.ContainsKey(e.To)).DistinctBy(e => (e.From, e.To, e.Kind, e.Label, e.Condition))];
        StructureKey = string.Join('\n', [.. Nodes.Select(n => n.Id), "--", .. Edges.Select(e => $"{e.From}>{e.To}:{e.Kind}:{e.Label}:{e.Condition}")]);
    }

    public static StoryGraph Empty { get; } = new([], []);

    public IReadOnlyList<StoryNode> Nodes { get; }

    public IReadOnlyList<StoryEdge> Edges { get; }

    /// <summary>
    /// Identifies the shape of the graph (nodes, edges and labels, not source positions), so a view
    /// only re-lays out when the structure actually changed.
    /// </summary>
    public string StructureKey { get; }

    public StoryNode? Find(string? id) => id is null ? null : _byId.GetValueOrDefault(id);

    public bool HasEdge(string from, string to) => Edges.Any(e => e.From == from && e.To == to);

    /// <summary>
    /// Maps a runtime path (e.g. <c>forest.clearing.2</c>, <c>town.square.0.c-1</c>, <c>0.3</c>) to the
    /// knot or stitch it is in. Returns null for content in functions.
    /// </summary>
    public string? NodeForRuntimePath(string? runtimePath)
    {
        if (string.IsNullOrEmpty(runtimePath))
        {
            return null;
        }

        // The leading named components are knot[.stitch]; indices and weave names follow.
        List<string> names = [];
        foreach (var component in runtimePath.Split('.'))
        {
            if (component.Length == 0 || char.IsDigit(component[0]) || component == "^")
            {
                break;
            }

            names.Add(component);
        }

        if (names.Count == 0)
        {
            return _byId.ContainsKey(StartId) ? StartId : null;
        }

        for (var length = Math.Min(names.Count, 2); length > 0; length--)
        {
            var id = string.Join('.', names.Take(length));
            if (_byId.ContainsKey(id))
            {
                return id;
            }
        }

        return null;
    }
}
