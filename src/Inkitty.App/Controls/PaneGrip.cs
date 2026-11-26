using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Dock.Model.Core;

namespace Inkitty.App.Controls;

/// <summary>
/// A slim handle at the top of a tool pane, replacing Dock's title bar: drag it to move the pane,
/// right-click it for float/close. It invisibly overlays the pane and shows a short bar on hover.
/// </summary>
public sealed class PaneGrip : TemplatedControl
{
    public static readonly StyledProperty<IDockable?> DockableProperty =
        AvaloniaProperty.Register<PaneGrip, IDockable?>(nameof(Dockable));

    // Dock starts a pane drag from a rendered control marked as a drag area whose DataContext is the
    // dockable; the template's background panel is that control (see Styles/Inkitty.axaml).
    public PaneGrip() => ContextMenu = BuildMenu();

    /// <summary>The tool this grip moves.</summary>
    public IDockable? Dockable
    {
        get => GetValue(DockableProperty);
        set => SetValue(DockableProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DockableProperty)
        {
            DataContext = Dockable;
            ToolTip.SetTip(this, Dockable is { Title: { Length: > 0 } title } ? $"{title} — drag to move" : null);
        }
    }

    private ContextMenu BuildMenu()
    {
        var floatItem = new MenuItem { Header = "Float" };
        floatItem.Click += (_, _) =>
        {
            if (Dockable is { Factory: { } factory } dockable && dockable.CanFloat)
            {
                factory.FloatDockable(dockable);
            }
        };

        var closeItem = new MenuItem { Header = "Close" };
        closeItem.Click += (_, _) =>
        {
            if (Dockable is { Factory: { } factory } dockable && dockable.CanClose)
            {
                factory.CloseDockable(dockable);
            }
        };

        return new ContextMenu { ItemsSource = new[] { floatItem, closeItem } };
    }
}
