using System.Globalization;
using Avalonia.Media;

namespace Inkitty.App.Theming;

internal static class ColorMath
{
    /// <summary>Parses VS Code colours (<c>#RGB</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>, alpha last).</summary>
    public static Color? ParseVsCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#')
        {
            return null;
        }

        var hex = value[1..];
        if (hex.Length is 3 or 4)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return null;
        }

        return hex.Length == 6
            ? Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Color.FromArgb((byte)v, (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8));
    }

    /// <summary>Linear blend: 0 = <paramref name="a"/>, 1 = <paramref name="b"/>. Result is opaque.</summary>
    public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Flattens a translucent colour onto an opaque background.</summary>
    public static Color Over(Color color, Color background) =>
        color.A == 255 ? color : Mix(background, Color.FromRgb(color.R, color.G, color.B), color.A / 255.0);

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
}
