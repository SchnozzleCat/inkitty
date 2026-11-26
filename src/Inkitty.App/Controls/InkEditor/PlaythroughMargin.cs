using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using Inkitty.Core.StoryMap;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>
/// Shows the playthrough in the editor: a bar beside lines the story has been through, an arrow at the
/// line it most recently output, and a dot beside each choice on offer. The current line is also tinted.
/// </summary>
public sealed class PlaythroughMargin : AbstractMargin, IBackgroundRenderer
{
    private const double MarginWidth = 10;
    private FileProgress _progress = FileProgress.None;

    public FileProgress Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            InvalidateVisual();
            TextView?.InvalidateLayer(Layer);
        }
    }

    public KnownLayer Layer => KnownLayer.Background;

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    private Color Accent =>
        this.TryFindResource("SystemAccentColor", ActualThemeVariant, out var value) && value is Color color
            ? color
            : Color.FromRgb(0x00, 0x78, 0xD4);

    public override void Render(DrawingContext context)
    {
        var textView = TextView;
        if (textView is null || !textView.VisualLinesValid || _progress.IsEmpty)
        {
            return;
        }

        var accent = Accent;
        var trail = new SolidColorBrush(accent, 0.45);
        var strong = new SolidColorBrush(accent);
        foreach (var line in textView.VisualLines)
        {
            var number = line.FirstDocumentLine.LineNumber;
            var top = line.VisualTop - textView.VerticalOffset;
            var middle = top + line.Height / 2;

            if (_progress.VisitedLines.Contains(number))
            {
                context.FillRectangle(trail, new Rect(2, top, 3, line.Height));
            }

            if (_progress.CurrentLine == number)
            {
                var arrow = new StreamGeometry();
                using (var g = arrow.Open())
                {
                    g.BeginFigure(new Point(1, middle - 5), true);
                    g.LineTo(new Point(MarginWidth - 1, middle));
                    g.LineTo(new Point(1, middle + 5));
                    g.EndFigure(true);
                }

                context.DrawGeometry(strong, null, arrow);
            }
            else if (_progress.ChoiceLines.Contains(number))
            {
                context.DrawEllipse(null, new Pen(strong, 1.5), new Point(MarginWidth / 2, middle), 3, 3);
            }
        }
    }

    /// <summary>Tints the current line across the whole text area.</summary>
    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_progress.CurrentLine is not { } current || !textView.VisualLinesValid)
        {
            return;
        }

        var tint = new SolidColorBrush(Accent, 0.12);
        foreach (var line in textView.VisualLines.Where(l => l.FirstDocumentLine.LineNumber == current))
        {
            drawingContext.FillRectangle(tint, new Rect(0, line.VisualTop - textView.VerticalOffset, textView.Bounds.Width, line.Height));
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

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();
}
