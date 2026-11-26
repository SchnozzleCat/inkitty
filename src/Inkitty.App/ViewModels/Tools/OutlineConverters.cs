using Avalonia.Data.Converters;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>Maps outline item kinds to style classes.</summary>
public static class OutlineConverters
{
    public static readonly IValueConverter IsWeave = new FuncValueConverter<string?, bool>(k => k == "weave");
}
