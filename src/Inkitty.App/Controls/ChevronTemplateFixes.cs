using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Inkitty.App.Controls;

/// <summary>
/// Swaps Fluent's filled chevrons (submenu arrows, tree expanders) for one small stroked chevron that
/// matches the other icons. Shape and size are set here because template values outrank style setters;
/// colours and the expanded rotation are in Inkitty.axaml.
/// </summary>
public static class ChevronTemplateFixes
{
    private const double Width = 6;
    private const double Height = 9;

    // Centred in its box (inset by half the stroke so the round caps aren't clipped), so it rotates in place.
    private static readonly Geometry Chevron = Geometry.Parse("M1.25,1 L4.75,4.5 L1.25,8");

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        TemplatedControl.TemplateAppliedEvent.AddClassHandler<MenuItem>(
            (_, e) =>
            {
                if (e.NameScope.Find<Avalonia.Controls.Shapes.Path>("PART_ChevronPath") is { } path)
                {
                    Apply(path);
                    path.Margin = new Thickness(16, 0, 0, 0);
                }
            },
            RoutingStrategies.Direct);

        // The tree expander's chevron lives in the expander toggle's own template.
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ToggleButton>(
            (toggle, e) =>
            {
                if (toggle.Name == "PART_ExpandCollapseChevron" && e.NameScope.Find<Avalonia.Controls.Shapes.Path>("ChevronPath") is { } path)
                {
                    Apply(path);
                }
            },
            RoutingStrategies.Direct);
    }

    private static void Apply(Avalonia.Controls.Shapes.Path path)
    {
        path.Data = Chevron;
        path.Fill = null;
        path.Stretch = Stretch.None;
        path.Width = Width;
        path.Height = Height;
        path.HorizontalAlignment = HorizontalAlignment.Center;
        path.VerticalAlignment = VerticalAlignment.Center;
    }
}
