using Avalonia;
using Inkitty.Core.StoryMap;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Layout.Layered;
using MsaglPoint = Microsoft.Msagl.Core.Geometry.Point;

namespace Inkitty.App.StoryMap;

/// <summary>A positioned knot/stitch box, in map coordinates.</summary>
public sealed record MapNode(string Id, string Title, string? Subtitle, StoryNodeKind Kind, Rect Bounds);

/// <summary>One piece of an edge's route.</summary>
public abstract record MapSegment(Point Start, Point End);

public sealed record MapLine(Point Start, Point End) : MapSegment(Start, End);

public sealed record MapBezier(Point Start, Point Control1, Point Control2, Point End) : MapSegment(Start, End);

/// <param name="ArrowTip">Where the arrowhead points (the route stops short of it).</param>
/// <param name="LabelCenter">Centre of the label (condition and/or choice text), if the edge has one.</param>
/// <param name="Condition">The condition guarding the edge as written, e.g. <c>{has_torch}</c>.</param>
public sealed record MapEdge(string From, string To, StoryEdgeKind Kind, string? Label, IReadOnlyList<MapSegment> Route, Point ArrowTip, Point? LabelCenter, string? Condition = null)
{
    public bool IsConditional => Condition is not null;
}

/// <summary>A laid-out <see cref="StoryGraph"/>: top-to-bottom, origin at the top left.</summary>
public sealed class StoryMapLayout
{
    public const double Padding = 24;

    private readonly Dictionary<string, MapNode> _byId;

    public StoryMapLayout(string structureKey, IReadOnlyList<MapNode> nodes, IReadOnlyList<MapEdge> edges, Size size)
    {
        StructureKey = structureKey;
        Nodes = nodes;
        Edges = edges;
        Size = size;
        _byId = nodes.ToDictionary(n => n.Id);
    }

    public static StoryMapLayout Empty { get; } = new(string.Empty, [], [], default);

    /// <summary>The <see cref="StoryGraph.StructureKey"/> this was computed for.</summary>
    public string StructureKey { get; }

    public IReadOnlyList<MapNode> Nodes { get; }

    public IReadOnlyList<MapEdge> Edges { get; }

    public Size Size { get; }

    public MapNode? Find(string? id) => id is not null ? _byId.GetValueOrDefault(id) : null;

    public MapNode? HitTest(Point point) => Nodes.LastOrDefault(n => n.Bounds.Inflate(2).Contains(point));
}

/// <summary>Lays out story graphs with MSAGL's layered (Sugiyama) algorithm. Thread-safe; slow for huge graphs, so run it off the UI thread.</summary>
public static class StoryMapLayoutEngine
{
    /// <summary>Text measurements, taken on the UI thread before layout.</summary>
    /// <param name="Node">Size of each node's box, by id.</param>
    /// <param name="Label">Size of each edge label, by <see cref="LabelKey"/>.</param>
    public sealed record Measurements(IReadOnlyDictionary<string, Size> Node, IReadOnlyDictionary<string, Size> Label);

    /// <summary>Identifies an edge's label (condition above choice text) for measuring; null if it has none.</summary>
    public static string? LabelKey(StoryEdge edge) =>
        edge.Label is null && edge.Condition is null ? null : $"{edge.Condition}\n{edge.Label}";

    public static string TitleOf(StoryNode node) => node.Kind switch
    {
        StoryNodeKind.Start => "Beginning",
        StoryNodeKind.End => "END",
        _ => node.Title,
    };

    /// <summary>Stitches show their knot underneath, as one graph can have many stitches with the same name.</summary>
    public static string? SubtitleOf(StoryNode node) => node.Kind == StoryNodeKind.Stitch ? node.Parent : null;

    public static StoryMapLayout Compute(StoryGraph graph, Measurements sizes, CancellationToken cancellationToken = default)
    {
        if (graph.Nodes.Count == 0)
        {
            return StoryMapLayout.Empty;
        }

        var geometry = new GeometryGraph();
        Dictionary<string, Node> nodes = [];
        foreach (var node in graph.Nodes)
        {
            var size = sizes.Node.GetValueOrDefault(node.Id, new Size(90, 30));
            var msaglNode = new Node(CurveFactory.CreateRectangleWithRoundedCorners(size.Width, size.Height, 6, 6, new MsaglPoint()), node.Id);
            nodes[node.Id] = msaglNode;
            geometry.Nodes.Add(msaglNode);
        }

        List<(StoryEdge Story, Edge Msagl)> edges = [];
        foreach (var edge in graph.Edges)
        {
            var msaglEdge = new Edge(nodes[edge.From], nodes[edge.To]);
            msaglEdge.EdgeGeometry.TargetArrowhead = new Arrowhead { Length = 7 };
            if (LabelKey(edge) is { } label && sizes.Label.TryGetValue(label, out var labelSize))
            {
                msaglEdge.Label = new Label(labelSize.Width, labelSize.Height, msaglEdge);
            }

            geometry.Edges.Add(msaglEdge);
            edges.Add((edge, msaglEdge));
        }

        var settings = new SugiyamaLayoutSettings
        {
            LayerSeparation = 36,
            NodeSeparation = 24,
            PackingAspectRatio = 0.8,
        };
        var cancel = new Microsoft.Msagl.Core.CancelToken();
        using var registration = cancellationToken.Register(() => cancel.Canceled = true);
        Microsoft.Msagl.Miscellaneous.LayoutHelpers.CalculateLayout(geometry, settings, cancel);
        cancellationToken.ThrowIfCancellationRequested();

        // MSAGL's y axis points up and its first layer is at the top: flip into screen coordinates.
        var box = geometry.BoundingBox;
        Point Map(MsaglPoint p) => new(p.X - box.Left + StoryMapLayout.Padding, box.Top - p.Y + StoryMapLayout.Padding);

        List<MapNode> mapNodes = [];
        foreach (var node in graph.Nodes)
        {
            var bounds = nodes[node.Id].BoundingBox;
            mapNodes.Add(new MapNode(node.Id, TitleOf(node), SubtitleOf(node), node.Kind, new Rect(Map(bounds.LeftTop), Map(bounds.RightBottom))));
        }

        List<MapEdge> mapEdges = [];
        foreach (var (story, msagl) in edges)
        {
            List<MapSegment> route = [];
            AddCurve(route, msagl.Curve, Map);
            var tip = msagl.EdgeGeometry.TargetArrowhead?.TipPosition is { } t ? Map(t) : route.LastOrDefault()?.End ?? default;
            Point? label = msagl.Label is { } l ? Map(l.Center) : null;
            mapEdges.Add(new MapEdge(story.From, story.To, story.Kind, story.Label, route, tip, label, story.Condition));
        }

        return new StoryMapLayout(graph.StructureKey, mapNodes, mapEdges, new Size(box.Width + 2 * StoryMapLayout.Padding, box.Height + 2 * StoryMapLayout.Padding));
    }

    private static void AddCurve(List<MapSegment> route, ICurve? curve, Func<MsaglPoint, Point> map)
    {
        switch (curve)
        {
            case null:
                return;
            case Curve composite:
                foreach (var segment in composite.Segments)
                {
                    AddCurve(route, segment, map);
                }

                break;
            case LineSegment line:
                route.Add(new MapLine(map(line.Start), map(line.End)));
                break;
            case CubicBezierSegment bezier:
                route.Add(new MapBezier(map(bezier.B(0)), map(bezier.B(1)), map(bezier.B(2)), map(bezier.B(3))));
                break;
            default:
                // Anything else (arcs, polylines): approximate with straight pieces.
                const int Steps = 12;
                var previous = map(curve.Start);
                for (var i = 1; i <= Steps; i++)
                {
                    var next = map(curve[curve.ParStart + (curve.ParEnd - curve.ParStart) * i / Steps]);
                    route.Add(new MapLine(previous, next));
                    previous = next;
                }

                break;
        }
    }
}
