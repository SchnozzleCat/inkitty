using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using Inkitty.Core.Diagnostics;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>Draws wavy underlines (and a faint line tint for errors) under issue markers.</summary>
public sealed class IssueMarkerRenderer : IBackgroundRenderer
{
    private TextSegmentCollection<IssueMarker>? _markers;

    public KnownLayer Layer => KnownLayer.Selection;

    public TextSegmentCollection<IssueMarker> Markers => _markers ?? throw new InvalidOperationException("Not attached to a document.");

    public void Attach(TextDocument document)
    {
        _markers?.Disconnect(document);
        _markers = new TextSegmentCollection<IssueMarker>(document);
    }

    public void SetIssues(TextDocument document, IEnumerable<Issue> issues)
    {
        if (_markers is null)
        {
            Attach(document);
        }

        Markers.Clear();
        foreach (var marker in IssueMarkerFactory.Create(document, issues))
        {
            Markers.Add(marker);
        }
    }

    public IEnumerable<IssueMarker> MarkersAt(int offset) =>
        _markers?.FindSegmentsContaining(offset) ?? (IEnumerable<IssueMarker>)[];

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_markers is null || _markers.Count == 0 || !textView.VisualLinesValid)
        {
            return;
        }

        var visualLines = textView.VisualLines;
        if (visualLines.Count == 0)
        {
            return;
        }

        var viewStart = visualLines[0].FirstDocumentLine.Offset;
        var viewEnd = visualLines[^1].LastDocumentLine.EndOffset;

        foreach (var marker in _markers.FindOverlappingSegments(viewStart, viewEnd - viewStart).OrderByDescending(m => m.Issue.Severity.Rank))
        {
            var tint = IssueBrushes.LineTint(marker.Issue.Severity);
            var pen = new Pen(IssueBrushes.For(marker.Issue.Severity), 1.2);

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, marker))
            {
                if (tint != Brushes.Transparent)
                {
                    drawingContext.FillRectangle(tint, new Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
                }

                drawingContext.DrawGeometry(null, pen, CreateWave(rect.BottomLeft, rect.BottomRight));
            }
        }
    }

    private static StreamGeometry CreateWave(Point start, Point end)
    {
        const double step = 2.5;
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var y = start.Y - 1;
        context.BeginFigure(new Point(start.X, y), false);
        var up = true;
        for (var x = start.X + step; x <= Math.Max(end.X, start.X + step * 2); x += step)
        {
            context.LineTo(new Point(x, y + (up ? -step / 1.5 : 0)));
            up = !up;
        }

        context.EndFigure(false);
        return geometry;
    }
}
