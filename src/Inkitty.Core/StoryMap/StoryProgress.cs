using Inkitty.Core.FileSystem;
using Inkitty.Core.Playback;

namespace Inkitty.Core.StoryMap;

/// <summary>
/// Where the current playthrough is, and how it got there, in terms of both the source (lines) and the
/// story graph (knots/stitches). Immutable; rebuilt after every replay or choice.
/// </summary>
public sealed class StoryProgress
{
    private readonly HashSet<(string From, string To)> _pathEdges;

    private StoryProgress(
        IReadOnlyList<string> path,
        IReadOnlyDictionary<string, int> visitCounts,
        StoryLocation? current,
        IReadOnlyList<StoryLocation> trail,
        IReadOnlyList<StoryLocation> offeredChoices,
        bool hasEnded)
    {
        Path = path;
        VisitCounts = visitCounts;
        Current = current;
        Trail = trail;
        OfferedChoices = offeredChoices;
        HasEnded = hasEnded;
        _pathEdges = [.. path.Zip(path.Skip(1))];
    }

    public static StoryProgress Empty { get; } = new([], new Dictionary<string, int>(), null, [], [], hasEnded: false);

    /// <summary>Knots/stitches in the order the playthrough entered them (repeats kept, consecutive duplicates collapsed).</summary>
    public IReadOnlyList<string> Path { get; }

    /// <summary>How many times each visited knot/stitch was entered.</summary>
    public IReadOnlyDictionary<string, int> VisitCounts { get; }

    /// <summary>The source line the story most recently output (where the reader "is").</summary>
    public StoryLocation? Current { get; }

    /// <summary>Every source line the playthrough produced output from, in order, without duplicates.</summary>
    public IReadOnlyList<StoryLocation> Trail { get; }

    /// <summary>Where the choices on offer right now were written.</summary>
    public IReadOnlyList<StoryLocation> OfferedChoices { get; }

    public bool HasEnded { get; }

    /// <summary>The knot/stitch the story is in now (<see cref="StoryGraph.EndId"/> once it has ended).</summary>
    public string? CurrentNode => Path.Count > 0 ? Path[^1] : null;

    public bool WasVisited(string nodeId) => VisitCounts.ContainsKey(nodeId);

    public int VisitCount(string nodeId) => VisitCounts.GetValueOrDefault(nodeId);

    /// <summary>True if the playthrough went directly from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public bool TookEdge(string from, string to) => _pathEdges.Contains((from, to));

    /// <param name="graph">The graph of the story being played.</param>
    /// <param name="events">Everything the player has shown, in order.</param>
    /// <param name="visitCount">The runtime's visit count for a knot/stitch path (more accurate than counting output).</param>
    public static StoryProgress Build(StoryGraph graph, IReadOnlyList<StoryEvent> events, Func<string, int>? visitCount = null)
    {
        List<string> path = [];
        List<StoryLocation> trail = [];
        HashSet<(string?, int)> seen = [];
        StoryLocation? current = null;
        IReadOnlyList<StoryLocation> offered = [];
        var ended = false;

        void Visit(StoryLocation? location)
        {
            if (location is null)
            {
                return;
            }

            if (seen.Add((location.File, location.Line)))
            {
                trail.Add(location);
            }

            if (graph.NodeForRuntimePath(location.RuntimePath) is { } node && (path.Count == 0 || path[^1] != node))
            {
                path.Add(node);
            }
        }

        foreach (var storyEvent in events)
        {
            offered = [];
            switch (storyEvent)
            {
                case TextOutput text:
                    Visit(text.Source);
                    current = text.Source ?? current;
                    break;
                case ChoiceTaken taken:
                    Visit(taken.Source);
                    break;
                case ChoicesOffered choices:
                    offered = [.. choices.Choices.Select(c => c.Source).OfType<StoryLocation>()];
                    break;
                case StoryEnded:
                    ended = true;
                    break;
            }
        }

        if (path.Count == 0 && events.Count > 0 && graph.Find(StoryGraph.StartId) is not null)
        {
            path.Add(StoryGraph.StartId);
        }

        Dictionary<string, int> counts = [];
        foreach (var node in graph.Nodes)
        {
            var count = node.Kind is StoryNodeKind.Knot or StoryNodeKind.Stitch && visitCount is not null
                ? visitCount(node.Id)
                : 0;
            count = Math.Max(count, path.Count(p => p == node.Id));
            if (count > 0)
            {
                counts[node.Id] = count;
            }
        }

        path = FillGaps(graph, path, counts);
        if (ended && graph.Find(StoryGraph.EndId) is not null && path.Count > 0 && graph.HasEdge(path[^1], StoryGraph.EndId))
        {
            path.Add(StoryGraph.EndId);
            counts[StoryGraph.EndId] = 1;
        }

        return new StoryProgress(path, counts, current, trail, offered, ended);
    }

    /// <summary>
    /// Knots that only divert (e.g. <c>=== town === -> square</c>) produce no output, so they're missing
    /// between the nodes we saw. Where two consecutive nodes aren't directly connected, insert the
    /// shortest chain of visited nodes that connects them.
    /// </summary>
    private static List<string> FillGaps(StoryGraph graph, List<string> path, Dictionary<string, int> counts)
    {
        if (path.Count < 2)
        {
            return path;
        }

        var outgoing = graph.Edges.ToLookup(e => e.From, e => e.To);
        List<string> filled = [path[0]];
        for (var i = 1; i < path.Count; i++)
        {
            var (from, to) = (path[i - 1], path[i]);
            if (!graph.HasEdge(from, to) && ShortestVisitedChain(outgoing, from, to, counts) is { } chain)
            {
                filled.AddRange(chain);
            }

            filled.Add(to);
        }

        return filled;
    }

    private static List<string>? ShortestVisitedChain(ILookup<string, string> outgoing, string from, string to, Dictionary<string, int> counts)
    {
        const int MaxDepth = 4;
        Dictionary<string, string> cameFrom = [];
        Queue<(string Node, int Depth)> queue = new([(from, 0)]);
        while (queue.TryDequeue(out var item))
        {
            if (item.Depth >= MaxDepth)
            {
                continue;
            }

            foreach (var next in outgoing[item.Node])
            {
                if (next == to)
                {
                    List<string> chain = [];
                    for (var n = item.Node; n != from; n = cameFrom[n])
                    {
                        chain.Insert(0, n);
                    }

                    return chain;
                }

                if (counts.ContainsKey(next) && next != from && cameFrom.TryAdd(next, item.Node))
                {
                    queue.Enqueue((next, item.Depth + 1));
                }
            }
        }

        return null;
    }

    /// <summary>The trail, offered choices and current line in one file, for editor decorations.</summary>
    public FileProgress ForFile(string file)
    {
        bool InFile(StoryLocation l) => l.File is { } f && PathComparer.PathEquals(f, file);
        return new FileProgress(
            Trail.Where(InFile).Select(l => l.Line).ToHashSet(),
            Current is { } c && InFile(c) && !HasEnded ? c.Line : null,
            OfferedChoices.Where(InFile).Select(l => l.Line).ToHashSet());
    }
}

/// <summary>Playthrough decorations for one file (1-based line numbers).</summary>
/// <param name="VisitedLines">Lines the story produced output from.</param>
/// <param name="CurrentLine">The line the story most recently output, if it's in this file.</param>
/// <param name="ChoiceLines">Lines with choices on offer right now.</param>
public sealed record FileProgress(IReadOnlySet<int> VisitedLines, int? CurrentLine, IReadOnlySet<int> ChoiceLines)
{
    public static FileProgress None { get; } = new(new HashSet<int>(), null, new HashSet<int>());

    public bool IsEmpty => VisitedLines.Count == 0 && CurrentLine is null && ChoiceLines.Count == 0;
}
