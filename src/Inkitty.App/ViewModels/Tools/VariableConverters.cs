using Avalonia.Data.Converters;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>Maps a variable's kind to style classes in the variables pane.</summary>
public static class VariableConverters
{
    public static readonly IValueConverter IsBool = Is("bool");
    public static readonly IValueConverter IsNumber = Is("number");
    public static readonly IValueConverter IsString = Is("string");
    public static readonly IValueConverter IsDivert = Is("divert");

    private static FuncValueConverter<string?, bool> Is(string kind) => new(k => k == kind);
}
