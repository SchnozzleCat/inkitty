using Avalonia;
using Avalonia.Media;

namespace Inkitty.App.Theming;

/// <summary>
/// The dock drop selectors (the buttons shown while dragging a pane), drawn in the theme's colours.
/// Dock's own are fixed light-blue bitmaps. Each is a 40x40 button: a pane outline whose
/// target side is filled with the accent, and an arrow pointing at it.
/// </summary>
public static class DockSelectorIcons
{
    /// <summary>The resource key for a selector (Top, Bottom, Left, Right, Center).</summary>
    public static string Key(string side) => $"InkittyDockSelector{side}";

    public static readonly string[] Sides = ["Top", "Bottom", "Left", "Right", "Center"];

    public static DrawingImage Create(string side, ThemeDefinition theme)
    {
        var c = theme.Colors;
        var contrast = theme.IsDark ? Colors.White : Colors.Black;
        var face = new SolidColorBrush(ColorMath.Mix(c.Background, contrast, 0.04));
        var edge = new SolidColorBrush(ColorMath.Mix(c.Background, contrast, 0.28));
        var muted = new SolidColorBrush(ColorMath.Mix(c.Foreground, c.Background, 0.4));
        var accent = new SolidColorBrush(c.Accent);

        var drawings = new DrawingGroup();
        drawings.Children.Add(new GeometryDrawing
        {
            Brush = face,
            Pen = new Pen(edge, 1),
            Geometry = new RectangleGeometry(new Rect(0.5, 0.5, 39, 39), 6, 6),
        });

        Rect? highlight = side switch
        {
            "Top" => new Rect(10, 10, 20, 8),
            "Bottom" => new Rect(10, 22, 20, 8),
            "Left" => new Rect(10, 10, 8, 20),
            "Right" => new Rect(22, 10, 8, 20),
            _ => new Rect(10, 10, 20, 20),
        };
        drawings.Children.Add(new GeometryDrawing
        {
            Brush = side == "Center" ? new SolidColorBrush(ColorMath.WithAlpha(c.Accent, 0x80)) : accent,
            Geometry = new RectangleGeometry(highlight.Value, 1.5, 1.5),
        });
        drawings.Children.Add(new GeometryDrawing
        {
            Pen = new Pen(muted, 1),
            Geometry = new RectangleGeometry(new Rect(10.5, 10.5, 19, 19), 1.5, 1.5),
        });

        // An arrow in the free part of the pane, pointing at the target side.
        Point[]? arrow = side switch
        {
            "Top" => [new(17, 26), new(23, 26), new(20, 22.5)],
            "Bottom" => [new(17, 14), new(23, 14), new(20, 17.5)],
            "Left" => [new(26, 17), new(26, 23), new(22.5, 20)],
            "Right" => [new(14, 17), new(14, 23), new(17.5, 20)],
            _ => null,
        };
        if (arrow is not null)
        {
            drawings.Children.Add(new GeometryDrawing { Brush = muted, Geometry = new PolylineGeometry(arrow, isFilled: true) });
        }

        return new DrawingImage(drawings);
    }
}
