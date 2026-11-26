using Avalonia.Data.Converters;
using Dock.Model.Core;

namespace Inkitty.App.Docking;

public static class DockConverters
{
    /// <summary>True if the dockable is in the main window (not already in a floating one): only then can it float.</summary>
    public static readonly IValueConverter IsInMainWindow = new FuncValueConverter<IDockable?, bool>(d =>
        d?.Factory is InkittyDockFactory factory && factory.IsInMainWindow(d));

    /// <summary>True if the tool can be auto-hidden where it is (see <see cref="InkittyDockFactory.CanPinTool"/>) and isn't already.</summary>
    public static readonly IValueConverter CanPin = new FuncValueConverter<IDockable?, bool>(d =>
        d?.Factory is InkittyDockFactory factory && factory.CanPinTool(d) && !factory.IsDockablePinned(d));
}
