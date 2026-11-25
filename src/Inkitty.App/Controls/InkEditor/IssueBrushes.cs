using Avalonia.Media;
using Inkitty.Core.Diagnostics;

namespace Inkitty.App.Controls.InkEditor;

internal static class IssueBrushes
{
    public static readonly IBrush Error = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35)).ToImmutable();
    public static readonly IBrush Warning = new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23)).ToImmutable();
    public static readonly IBrush Todo = new SolidColorBrush(Color.FromRgb(0x42, 0x8B, 0xF5)).ToImmutable();
    public static readonly IBrush Runtime = new SolidColorBrush(Color.FromRgb(0xC0, 0x4C, 0xD8)).ToImmutable();

    public static IBrush For(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Error => Error,
        IssueSeverity.Warning => Warning,
        IssueSeverity.Todo => Todo,
        _ => Runtime,
    };

    public static IBrush LineTint(IssueSeverity severity) => severity switch
    {
        IssueSeverity.Error => new SolidColorBrush(Color.FromArgb(0x22, 0xE5, 0x39, 0x35)),
        IssueSeverity.RuntimeError => new SolidColorBrush(Color.FromArgb(0x1C, 0xC0, 0x4C, 0xD8)),
        _ => Brushes.Transparent,
    };
}
