using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Dock.Avalonia.Controls;
using Dock.Settings;

namespace Inkitty.App.Docking;

/// <summary>Adjustments to Dock's control templates that styles can't make (template values outrank style setters).</summary>
public static class DockTemplateFixes
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        // A tool pane's tab strip runs along its bottom, so at the window's bottom edge the pointer is over one.
        // Dock marks the strip's empty area "indicators only", which also hides the window-edge selectors: the
        // bottom one vanished as the pointer reached it. Show the pane's full selectors there instead.
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<ToolTabStrip>(
            (_, e) => e.NameScope.Find<Border>("PART_BorderFill")?.SetValue(DockProperties.ShowDockIndicatorOnlyProperty, false),
            RoutingStrategies.Direct);

        // A floating window's own pin button (Dock binds its visibility in the template): a window holding
        // one pane is never auto-hidden, as that would leave it empty; with several panes, each pane's own
        // header has a pin (see InkittyDockFactory.CanPinTool).
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<MdiDocumentWindow>(
            (_, e) => e.NameScope.Find<Button>("PART_ToolPinButton")?.SetValue(Visual.IsVisibleProperty, false),
            RoutingStrategies.Direct);
    }
}
