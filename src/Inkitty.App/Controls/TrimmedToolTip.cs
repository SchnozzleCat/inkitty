using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Inkitty.App.Controls;

/// <summary>
/// <c>controls:TrimmedToolTip.IsEnabled="True"</c> on a trimmed <see cref="TextBlock"/> shows its full text
/// as a tooltip, but only when the text is actually cut off.
/// </summary>
public static class TrimmedToolTip
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("IsEnabled", typeof(TrimmedToolTip));

    static TrimmedToolTip() =>
        IsEnabledProperty.Changed.AddClassHandler<TextBlock>((block, e) =>
        {
            if (e.NewValue is true)
            {
                ToolTip.AddToolTipOpeningHandler(block, OnOpening);
            }
            else
            {
                ToolTip.RemoveToolTipOpeningHandler(block, OnOpening);
            }
        });

    public static bool GetIsEnabled(TextBlock element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(TextBlock element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>True if the text block is showing an ellipsis instead of part of its text.</summary>
    public static bool IsTrimmed(TextBlock block) => block.TextLayout.TextLines.Any(l => l.HasCollapsed);

    private static void OnOpening(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is not TextBlock block)
        {
            return;
        }

        if (IsTrimmed(block))
        {
            ToolTip.SetTip(block, block.Text);
        }
        else
        {
            e.Cancel = true;
        }
    }
}
