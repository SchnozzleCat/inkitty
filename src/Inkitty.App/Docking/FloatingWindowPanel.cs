using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Inkitty.App.Docking;

/// <summary>
/// Lays out the in-app floating windows (Dock's <see cref="MdiLayoutPanel"/>), measuring each window at its own
/// size. Dock measures every window at the size of the whole main window; a window's content then keeps those
/// sizes (a grid's star row remembers the tallest content it was measured with), so panes were laid out larger
/// than the window and cut off: a pane split below another ended up past the bottom edge, out of sight.
/// </summary>
public sealed class FloatingWindowPanel : MdiLayoutPanel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(SizeFor(child, availableSize));
        }

        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // A window moved or resized only asks for a new arrange, so measure again at its new size first.
        foreach (var child in Children)
        {
            child.Measure(SizeFor(child, finalSize));
        }

        return base.ArrangeOverride(finalSize);
    }

    private static Size SizeFor(Control child, Size available)
    {
        if (child.DataContext is not IMdiDocument { MdiState: MdiWindowState.Normal } document
            || document.MdiBounds is not { Width: > 0, Height: > 0 } bounds
            || double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height))
        {
            return available;
        }

        return new Size(Math.Min(bounds.Width, available.Width), Math.Min(bounds.Height, available.Height));
    }
}
