using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Inkitty.App.Theming;
using Inkitty.Core.StoryMap;

namespace Inkitty.App.StoryMap;

/// <summary>
/// Draws a <see cref="StoryGraph"/> as a flow chart and overlays the current playthrough: visited knots
/// are filled, the path taken is drawn in the accent colour, and the current knot is highlighted.
/// Wheel zooms, drag pans, clicking a knot raises <see cref="NodeActivated"/>.
/// </summary>
/// <remarks>
/// The map is drawn once onto an inner layer in map coordinates, and redrawn only when what it shows
/// changes (layout, progress, hover, theme). Panning and zooming just move and scale that layer, which
/// the compositor does without running any of this code: in the browser, where .NET is interpreted,
/// redrawing a large story on every mouse move made dragging lag.
/// </remarks>
#pragma warning disable CA1001 // the CTS is disposed when the control leaves the visual tree; controls aren't IDisposable
public sealed class StoryMapControl : Control
#pragma warning restore CA1001
{
    public static readonly StyledProperty<StoryGraph?> GraphProperty =
        AvaloniaProperty.Register<StoryMapControl, StoryGraph?>(nameof(Graph));

    public static readonly StyledProperty<StoryProgress?> ProgressProperty =
        AvaloniaProperty.Register<StoryMapControl, StoryProgress?>(nameof(Progress));

    /// <summary>Keep the current knot in view as the story moves.</summary>
    public static readonly StyledProperty<bool> FollowCurrentProperty =
        AvaloniaProperty.Register<StoryMapControl, bool>(nameof(FollowCurrent), true);

    private const double MinZoom = 0.15;

    /// <summary>Below this the text is too small to read, so the automatic view zooms in and centres on the current knot instead.</summary>
    private const double ReadableZoom = 0.8;
    private const double MaxZoom = 3;
    private const double NodeFontSize = 12.5;
    private const double SubtitleFontSize = 10;
    private const double LabelFontSize = 10.5;

    private StoryMapLayout _layout = StoryMapLayout.Empty;
    private CancellationTokenSource? _layoutCts;
    private Vector _offset;
    private double _zoom = 1;
    /// <summary>Until the user pans or zooms, the map keeps fitting itself to the view.</summary>
    private bool _userMoved;
    private Point? _dragStart;
    private Vector _dragOffset;
    private bool _dragged;
    private string? _hover;
    private readonly MapLayer _map;

    /// <summary>
    /// Holds <see cref="_map"/> and carries the pan/zoom transform. A control redraws when its own transform
    /// changes, so the transform sits on this empty parent: moving it leaves the map's drawing untouched.
    /// </summary>
    private readonly Panel _viewport = new() { RenderTransformOrigin = RelativePoint.TopLeft };

    static StoryMapControl()
    {
        FocusableProperty.OverrideDefaultValue<StoryMapControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<StoryMapControl>(true);
    }

    public StoryMapControl()
    {
        _map = new MapLayer(this);
        _viewport.Children.Add(_map);
        VisualChildren.Add(_viewport);
        ActualThemeVariantChanged += (_, _) => RedrawAll();
        ResourcesChanged += (_, _) => RedrawAll(); // theme colours
        ApplyView();
    }

    /// <summary>The map's contents, drawn in map coordinates; <see cref="ApplyView"/> places it in the view.</summary>
    private sealed class MapLayer(StoryMapControl owner) : Control
    {
        public override void Render(DrawingContext context) => owner.DrawMap(context);
    }

    /// <summary>Moves and scales the map layer to the current pan and zoom (no redraw).</summary>
    private void ApplyView()
    {
        _viewport.RenderTransform = new MatrixTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset));
    }

    /// <summary>Redraws the map's contents (what it shows changed, not where).</summary>
    private void RedrawMap() => _map.InvalidateVisual();

    /// <summary>Redraws the background and the map (the colours changed).</summary>
    private void RedrawAll()
    {
        InvalidateVisual();
        RedrawMap();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _viewport.Measure(_layout.Size);
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _viewport.Arrange(new Rect(_layout.Size));
        return finalSize;
    }

    public StoryGraph? Graph
    {
        get => GetValue(GraphProperty);
        set => SetValue(GraphProperty, value);
    }

    public StoryProgress? Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public bool FollowCurrent
    {
        get => GetValue(FollowCurrentProperty);
        set => SetValue(FollowCurrentProperty, value);
    }

    /// <summary>The current layout (exposed for tests).</summary>
    public StoryMapLayout MapLayout => _layout;

    public double Zoom => _zoom;

    /// <summary>Raised when the user clicks a knot or stitch.</summary>
    public event EventHandler<string>? NodeActivated;

    /// <summary>Raised when a new layout has been applied.</summary>
    public event EventHandler? MapLayoutUpdated;

    public void ZoomBy(double factor) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    /// <summary>Scales and centres the whole map in the view (and keeps doing so as the view resizes).</summary>
    public void FitToView()
    {
        _userMoved = false;
        if (_layout.Nodes.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        _zoom = Math.Clamp(Math.Min(Bounds.Width / _layout.Size.Width, Bounds.Height / _layout.Size.Height), MinZoom, 1.25);
        _offset = new Vector((Bounds.Width - _layout.Size.Width * _zoom) / 2, (Bounds.Height - _layout.Size.Height * _zoom) / 2);
        ApplyView();
    }

    /// <summary>
    /// The view before the user pans or zooms: the whole map if it's readable at that size, otherwise
    /// a readable zoom centred on the current knot.
    /// </summary>
    private void AutoView()
    {
        if (_layout.Nodes.Count == 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var fit = Math.Min(Bounds.Width / _layout.Size.Width, Bounds.Height / _layout.Size.Height);
        if (fit >= ReadableZoom)
        {
            FitToView();
            return;
        }

        _zoom = ReadableZoom;
        CenterOn(Progress?.CurrentNode ?? StoryGraph.StartId);
    }

    /// <summary>Scrolls a node into the middle of the view.</summary>
    public void CenterOn(string id)
    {
        if (_layout.Find(id) is not { } node)
        {
            return;
        }

        _offset = new Vector(Bounds.Width / 2 - node.Bounds.Center.X * _zoom, Bounds.Height / 2 - node.Bounds.Center.Y * _zoom);
        ApplyView();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GraphProperty)
        {
            RequestLayout();
        }
        else if (change.Property == ProgressProperty)
        {
            RedrawMap();
            if (FollowCurrent)
            {
                BringCurrentIntoView();
            }
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!_userMoved)
        {
            AutoView();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestLayout(); // a layout cancelled by detaching (the pane moving) is started again; no-op if current
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _layoutCts?.Cancel();
        _layoutCts?.Dispose();
        _layoutCts = null;
    }

    private void RequestLayout()
    {
        var graph = Graph ?? StoryGraph.Empty;
        // A newer graph always wins over one still being laid out. Without this, a graph that briefly goes
        // away and comes back (the map pane moving between windows rebinds it) left the "no graph" layout
        // running, and it replaced the map with an empty one.
        _layoutCts?.Cancel();
        _layoutCts?.Dispose();
        _layoutCts = null;
        if (graph.StructureKey == _layout.StructureKey)
        {
            return; // same shape (e.g. text edits): keep the layout and the view
        }

        var cts = _layoutCts = new CancellationTokenSource();
        var sizes = Measure(graph);
        _ = Task.Run(() =>
        {
            StoryMapLayout layout;
            try
            {
                layout = StoryMapLayoutEngine.Compute(graph, sizes, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NullReferenceException or IndexOutOfRangeException)
            {
                System.Diagnostics.Trace.TraceWarning($"Story map layout failed: {ex}");
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                // Keep whatever the user was looking at in place, if it still exists.
                var anchor = _hover ?? Progress?.CurrentNode;
                var before = _layout.Find(anchor)?.Bounds.Center;
                _layout = layout;
                if (!_userMoved)
                {
                    AutoView();
                }
                else if (before is { } b && layout.Find(anchor)?.Bounds.Center is { } a)
                {
                    _offset += (b - a) * _zoom;
                }

                if (FollowCurrent)
                {
                    BringCurrentIntoView();
                }

                ApplyView();
                InvalidateMeasure(); // the layer takes the new map's size
                RedrawMap();
                MapLayoutUpdated?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private void BringCurrentIntoView()
    {
        if (Progress?.CurrentNode is not { } id || _layout.Find(id) is not { } node || Bounds.Width <= 0)
        {
            return;
        }

        var onScreen = new Rect(node.Bounds.Position * _zoom + _offset, node.Bounds.Size * _zoom);
        if (!new Rect(Bounds.Size).Deflate(24).Contains(onScreen))
        {
            CenterOn(id);
        }
    }

    private static StoryMapLayoutEngine.Measurements Measure(StoryGraph graph)
    {
        Dictionary<string, Size> nodes = [];
        foreach (var node in graph.Nodes)
        {
            var title = Text(StoryMapLayoutEngine.TitleOf(node), NodeFontSize, FontWeight.SemiBold, Brushes.Black);
            var width = title.Width;
            var height = title.Height;
            if (StoryMapLayoutEngine.SubtitleOf(node) is { } subtitle)
            {
                var sub = Text(subtitle, SubtitleFontSize, FontWeight.Normal, Brushes.Black);
                width = Math.Max(width, sub.Width);
                height += sub.Height;
            }

            nodes[node.Id] = new Size(Math.Max(56, width + 24), height + 14);
        }

        Dictionary<string, Size> labels = [];
        foreach (var edge in graph.Edges)
        {
            if (StoryMapLayoutEngine.LabelKey(edge) is not { } key || labels.ContainsKey(key))
            {
                continue;
            }

            var (condition, choice) = LabelLines(edge.Condition, edge.Label, Brushes.Black, Brushes.Black, FontWeight.SemiBold);
            labels[key] = new Size(Math.Max(condition?.Width ?? 0, choice?.Width ?? 0) + 8, (condition?.Height ?? 0) + (choice?.Height ?? 0) + 2);
        }

        return new(nodes, labels);
    }

    // ---- rendering ----

    private sealed record Palette(
        IBrush Background,
        IBrush Text,
        IBrush Muted,
        Color Accent,
        IBrush NodeFill,
        IPen NodeBorder,
        IPen Edge,
        IPen PathEdge,
        IBrush LabelBackground,
        IBrush Condition);

    private Palette CurrentPalette()
    {
        Color Find(string key, Color fallback) => this.TryFindResource(key, ActualThemeVariant, out var value) switch
        {
            true when value is Color c => c,
            true when value is ISolidColorBrush b => b.Color,
            _ => fallback,
        };

        var background = Find("InkittyEditorBackground", Colors.White);
        var text = Find("SystemControlForegroundBaseHighBrush", Colors.Black);
        var accent = Find("SystemAccentColor", Color.FromRgb(0x00, 0x78, 0xD4));
        var dark = ColorMath.Luminance(background) < 0.5;
        var muted = ColorMath.Mix(text, background, 0.55);
        return new Palette(
            new SolidColorBrush(background),
            new SolidColorBrush(text),
            new SolidColorBrush(muted),
            accent,
            new SolidColorBrush(ColorMath.Mix(background, dark ? Colors.White : Colors.Black, 0.05)),
            new Pen(new SolidColorBrush(ColorMath.Mix(text, background, 0.7)), 1),
            new Pen(new SolidColorBrush(ColorMath.Mix(text, background, 0.6)), 1.1),
            new Pen(new SolidColorBrush(accent), 2.4),
            new SolidColorBrush(background, 0.85),
            new SolidColorBrush(Find("InkittyValueKeyword", accent)));
    }

    /// <summary>How many times the map's nodes and edges have been drawn (exposed for tests).</summary>
    internal int MapDrawCount { get; private set; }

    /// <summary>The background (also makes the whole control take pointer input); the map is a layer above it.</summary>
    public override void Render(DrawingContext context) =>
        context.FillRectangle(CurrentPalette().Background, new Rect(Bounds.Size));

    /// <summary>Draws the map's contents in map coordinates (the layer's transform places them).</summary>
    private void DrawMap(DrawingContext context)
    {
        MapDrawCount++;
        if (_layout.Nodes.Count == 0)
        {
            return;
        }

        var palette = CurrentPalette();
        var progress = Progress ?? StoryProgress.Empty;

        // Edges the story didn't take first, then the path on top.
        foreach (var edge in _layout.Edges.OrderBy(e => progress.TookEdge(e.From, e.To)))
        {
            DrawEdge(context, edge, progress.TookEdge(edge.From, edge.To), palette);
        }

        foreach (var node in _layout.Nodes)
        {
            DrawNode(context, node, progress, palette);
        }
    }

    // Conditional ways out are dashed; tunnels/threads (which return) dash-dot; auto-entered stitches dotted.
    private static readonly IDashStyle Dashed = new Avalonia.Media.Immutable.ImmutableDashStyle([4, 3], 0);
    private static readonly IDashStyle DashDot = new Avalonia.Media.Immutable.ImmutableDashStyle([6, 3, 1, 3], 0);
    private static readonly IDashStyle Dotted = new Avalonia.Media.Immutable.ImmutableDashStyle([1, 3], 0);

    /// <summary>The dash pattern for an edge (null = solid); exposed for tests.</summary>
    internal static IDashStyle? DashFor(MapEdge edge) => edge switch
    {
        { Kind: StoryEdgeKind.Tunnel or StoryEdgeKind.Thread } => DashDot,
        { IsConditional: true } => Dashed,
        { Kind: StoryEdgeKind.Auto } => Dotted,
        _ => null,
    };

    private static void DrawEdge(DrawingContext context, MapEdge edge, bool taken, Palette palette)
    {
        var basePen = taken ? palette.PathEdge : palette.Edge;
        var pen = DashFor(edge) is { } dash ? new Pen(basePen.Brush, basePen.Thickness, dash) : basePen;

        if (edge.Route.Count > 0)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(edge.Route[0].Start, false);
                foreach (var segment in edge.Route)
                {
                    switch (segment)
                    {
                        case MapBezier b:
                            g.CubicBezierTo(b.Control1, b.Control2, b.End);
                            break;
                        default:
                            g.LineTo(segment.End);
                            break;
                    }
                }

                g.LineTo(edge.ArrowTip);
                g.EndFigure(false);
            }

            context.DrawGeometry(null, pen, geometry);
            DrawArrowhead(context, edge.Route[^1].End, edge.ArrowTip, basePen.Brush!);
        }

        if (edge.LabelCenter is { } center && (edge.Label is not null || edge.Condition is not null))
        {
            var (condition, choice) = LabelLines(
                edge.Condition,
                edge.Label,
                palette.Condition,
                taken ? palette.Text : palette.Muted,
                taken ? FontWeight.SemiBold : FontWeight.Normal);
            var width = Math.Max(condition?.Width ?? 0, choice?.Width ?? 0);
            var height = (condition?.Height ?? 0) + (choice?.Height ?? 0);
            var box = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
            context.FillRectangle(palette.LabelBackground, box.Inflate(new Thickness(3, 0)), 3);

            // The condition sits above the choice text, both centred.
            var y = box.Top;
            foreach (var line in (IEnumerable<FormattedText?>)[condition, choice])
            {
                if (line is not null)
                {
                    context.DrawText(line, new Point(center.X - line.Width / 2, y));
                    y += line.Height;
                }
            }
        }
    }

    /// <summary>A label's lines: the condition in a monospace code colour, then the choice text.</summary>
    private static (FormattedText? Condition, FormattedText? Choice) LabelLines(string? condition, string? choice, IBrush conditionBrush, IBrush choiceBrush, FontWeight choiceWeight) =>
        (condition is null ? null : new FormattedText(condition, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, ConditionTypeface, LabelFontSize - 0.5, conditionBrush),
         choice is null ? null : Text(choice, LabelFontSize, choiceWeight, choiceBrush));

    private static readonly Typeface ConditionTypeface = new(new FontFamily(Inkitty.Core.Settings.AppSettings.DefaultEditorFont));

    private static void DrawArrowhead(DrawingContext context, Point from, Point tip, IBrush brush)
    {
        var direction = tip - from;
        var length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (length < 0.01)
        {
            return;
        }

        var unit = direction / length;
        var normal = new Vector(-unit.Y, unit.X);
        var back = tip - unit * 7;
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(tip, true);
            g.LineTo(back + normal * 3.5);
            g.LineTo(back - normal * 3.5);
            g.EndFigure(true);
        }

        context.DrawGeometry(brush, null, arrow);
    }

    private void DrawNode(DrawingContext context, MapNode node, StoryProgress progress, Palette palette)
    {
        var isCurrent = progress.CurrentNode == node.Id && !(progress.HasEnded && node.Kind != StoryNodeKind.End);
        var visits = progress.VisitCount(node.Id);
        var visited = visits > 0 || progress.WasVisited(node.Id);
        var radius = node.Kind is StoryNodeKind.Start or StoryNodeKind.End ? node.Bounds.Height / 2 : 6;

        IBrush fill;
        IPen border;
        IBrush text;
        if (isCurrent)
        {
            // Soft halo, then a solid accent box.
            context.DrawRectangle(new SolidColorBrush(palette.Accent, 0.25), null, node.Bounds.Inflate(5), radius + 5, radius + 5);
            fill = new SolidColorBrush(palette.Accent);
            border = new Pen(new SolidColorBrush(palette.Accent), 1.5);
            text = ColorMath.Luminance(palette.Accent) > 0.6 ? Brushes.Black : Brushes.White;
        }
        else if (visited)
        {
            fill = new SolidColorBrush(ColorMath.Mix(((ISolidColorBrush)palette.Background).Color, palette.Accent, 0.22));
            border = new Pen(new SolidColorBrush(palette.Accent), 1.5);
            text = palette.Text;
        }
        else
        {
            fill = palette.NodeFill;
            border = palette.NodeBorder;
            text = palette.Muted;
        }

        if (_hover == node.Id)
        {
            border = new Pen(border.Brush, border.Thickness + 1);
        }

        context.DrawRectangle(fill, border, node.Bounds, radius, radius);

        var title = Text(node.Title, NodeFontSize, FontWeight.SemiBold, text);
        var subtitle = node.Subtitle is { } s ? Text(s, SubtitleFontSize, FontWeight.Normal, text) : null;
        var height = title.Height + (subtitle?.Height ?? 0);
        var y = node.Bounds.Center.Y - height / 2;
        context.DrawText(title, new Point(node.Bounds.Center.X - title.Width / 2, y));
        if (subtitle is not null)
        {
            using (context.PushOpacity(0.75))
            {
                context.DrawText(subtitle, new Point(node.Bounds.Center.X - subtitle.Width / 2, y + title.Height));
            }
        }

        if (visits > 1)
        {
            var badge = Text($"×{visits}", 9.5, FontWeight.Bold, Brushes.White);
            var badgeRect = new Rect(node.Bounds.Right - badge.Width - 2, node.Bounds.Top - badge.Height / 2 - 1, badge.Width + 8, badge.Height + 2);
            context.DrawRectangle(new SolidColorBrush(ColorMath.Mix(palette.Accent, Colors.Black, 0.2)), null, badgeRect, badgeRect.Height / 2, badgeRect.Height / 2);
            context.DrawText(badge, new Point(badgeRect.X + 4, badgeRect.Y + 1));
        }
    }

    private static FormattedText Text(string text, double size, FontWeight weight, IBrush brush) =>
        new(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight), size, brush);

    // ---- interaction ----

    private Point ToMap(Point view) => new((view.X - _offset.X) / _zoom, (view.Y - _offset.Y) / _zoom);

    private void ZoomAt(Point view, double factor)
    {
        var zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var anchor = ToMap(view);
        _zoom = zoom;
        _offset = new Vector(view.X - anchor.X * zoom, view.Y - anchor.Y * zoom);
        _userMoved = true;
        ApplyView();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta) || Math.Abs(e.Delta.X) < 0.01)
        {
            ZoomAt(e.GetPosition(this), Math.Pow(1.15, e.Delta.Y));
        }
        else
        {
            _offset += new Vector(e.Delta.X, e.Delta.Y) * 40;
            _userMoved = true;
            ApplyView();
        }

        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        _dragStart = e.GetPosition(this);
        _dragOffset = _offset;
        _dragged = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (_dragStart is { } start)
        {
            var delta = position - start;
            if (_dragged || Math.Abs(delta.X) + Math.Abs(delta.Y) > 4)
            {
                if (!_dragged)
                {
                    _dragged = true;
                    Cursor = new Cursor(StandardCursorType.SizeAll);
                }

                _userMoved = true;
                _offset = _dragOffset + delta;
                ApplyView();
            }

            return;
        }

        var hover = _layout.HitTest(ToMap(position))?.Id;
        if (hover != _hover)
        {
            _hover = hover;
            Cursor = hover is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
            ToolTip.SetTip(this, hover is null ? null : TooltipFor(hover));
            RedrawMap();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragStart is null)
        {
            return;
        }

        _dragStart = null;
        e.Pointer.Capture(null);
        Cursor = _hover is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);
        if (!_dragged && _layout.HitTest(ToMap(e.GetPosition(this))) is { } node)
        {
            NodeActivated?.Invoke(this, node.Id);
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // Capturing the pointer to drag reports leaving the map; keep the hover (and the drawing) as it is.
        if (_dragStart is not null)
        {
            return;
        }

        if (_hover is not null)
        {
            _hover = null;
            RedrawMap();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.OemPlus or Key.Add:
                ZoomBy(1.25);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomBy(0.8);
                break;
            case Key.D0 or Key.NumPad0:
                FitToView();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private string TooltipFor(string id)
    {
        var progress = Progress ?? StoryProgress.Empty;
        var name = id switch
        {
            StoryGraph.StartId => "Beginning of the story (top of the main file)",
            StoryGraph.EndId => "END",
            _ => id,
        };
        var visits = progress.VisitCount(id);
        var state = progress.CurrentNode == id ? "You are here" : visits switch
        {
            0 => "Not visited in this playthrough",
            1 => "Visited once",
            _ => $"Visited {visits} times",
        };
        return Graph?.Find(id)?.Range is { } range ? $"{name}\n{state}\n{range.File}:{range.Line} — click to open" : $"{name}\n{state}";
    }
}
