using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Inkitty.Core.Diagnostics;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>A narrow margin with a coloured dot per line that has issues; hovering shows the messages.</summary>
public sealed class IssueGutterMargin : AbstractMargin
{
    private const double Size = 14;
    private readonly IssueMarkerRenderer _renderer;

    public IssueGutterMargin(IssueMarkerRenderer renderer)
    {
        _renderer = renderer;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, 0);

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid || Document is null)
        {
            return;
        }

        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        foreach (var line in textView.VisualLines)
        {
            var worst = WorstOn(line.FirstDocumentLine.LineNumber);
            if (worst is not { } severity)
            {
                continue;
            }

            var y = line.VisualTop - textView.VerticalOffset + line.Height / 2;
            var brush = IssueBrushes.For(severity);
            if (severity == IssueSeverity.Todo)
            {
                context.DrawRectangle(brush, null, new Rect(3, y - 3.5, 7, 7), 1.5, 1.5);
            }
            else
            {
                context.DrawEllipse(brush, null, new Point(Size / 2, y), 4, 4);
            }
        }
    }

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
        }

        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var textView = TextView;
        if (textView is null || Document is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var line = textView.GetVisualLineFromVisualTop(position.Y + textView.VerticalOffset);
        var messages = line is null ? [] : IssuesOn(line.FirstDocumentLine.LineNumber).Where(i => i.Severity != IssueSeverity.Todo).Select(i => $"{Label(i.Severity)}: {i.Message}").ToList();
        ToolTip.SetTip(this, messages.Count > 0 ? string.Join("\n", messages) : null);
        ToolTip.SetIsOpen(this, messages.Count > 0);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetIsOpen(this, false);
    }

    internal static string Label(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Error => "Error",
        IssueSeverity.Warning => "Warning",
        IssueSeverity.Todo => "TODO",
        IssueSeverity.RuntimeError => "Runtime error",
        _ => "Runtime warning",
    };

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    private IEnumerable<Issue> IssuesOn(int lineNumber)
    {
        if (Document is null || lineNumber > Document.LineCount)
        {
            return [];
        }

        // Only the line's own text: including the delimiter would also match markers starting on the next line.
        var line = Document.GetLineByNumber(lineNumber);
        return _renderer.Markers
            .FindOverlappingSegments(line.Offset, line.Length)
            .Where(m => m.StartOffset <= line.EndOffset && m.EndOffset >= line.Offset && (m.StartOffset < line.EndOffset || line.Length == 0 || m.Length == 0))
            .Select(m => m.Issue);
    }

    private IssueSeverity? WorstOn(int lineNumber) =>
        IssuesOn(lineNumber).OrderBy(i => i.Severity.Rank).Select(i => (IssueSeverity?)i.Severity).FirstOrDefault();
}
