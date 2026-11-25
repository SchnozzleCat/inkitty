using System.Security.Cryptography;
using System.Text;

namespace Inkitty.Core.FileSystem;

public enum LineEnding
{
    Lf,
    CrLf,
}

/// <summary>How a text file was stored on disk, so saving can round-trip it.</summary>
public readonly record struct TextFormat(LineEnding LineEnding, bool HasBom)
{
    public static TextFormat Default { get; } = new(OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf, HasBom: false);
}

/// <summary>
/// Converts between bytes on disk and the in-memory representation, which always uses
/// <c>\n</c> line endings (so line numbers and columns are identical on every OS).
/// </summary>
public static class TextFileCodec
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public static (string Text, TextFormat Format) Decode(ReadOnlySpan<byte> bytes)
    {
        var hasBom = bytes.StartsWith(Utf8Bom);
        var raw = Utf8NoBom.GetString(hasBom ? bytes[Utf8Bom.Length..] : bytes);

        var crlf = 0;
        var lf = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\n')
            {
                continue;
            }

            if (i > 0 && raw[i - 1] == '\r')
            {
                crlf++;
            }
            else
            {
                lf++;
            }
        }

        var lineEnding = crlf == 0 && lf == 0
            ? TextFormat.Default.LineEnding
            : crlf > lf ? LineEnding.CrLf : LineEnding.Lf;

        return (NormalizeNewlines(raw), new TextFormat(lineEnding, hasBom));
    }

    public static byte[] Encode(string text, TextFormat format)
    {
        var normalized = NormalizeNewlines(text);
        var withEndings = format.LineEnding == LineEnding.CrLf ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal) : normalized;
        var body = Utf8NoBom.GetBytes(withEndings);
        return format.HasBom ? [.. Utf8Bom, .. body] : body;
    }

    public static string NormalizeNewlines(string text) =>
        text.Contains('\r', StringComparison.Ordinal)
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : text;

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
