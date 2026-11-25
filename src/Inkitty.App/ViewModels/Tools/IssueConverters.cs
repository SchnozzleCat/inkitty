using Avalonia.Data.Converters;
using Inkitty.Core.Diagnostics;

namespace Inkitty.App.ViewModels.Tools;

public static class IssueConverters
{
    public static readonly IValueConverter IsError = new FuncValueConverter<IssueSeverity, bool>(s => s.IsError);

    public static readonly IValueConverter IsWarning = new FuncValueConverter<IssueSeverity, bool>(s => s.IsWarning);

    public static readonly IValueConverter IsTodo = new FuncValueConverter<IssueSeverity, bool>(s => s == IssueSeverity.Todo);
}
