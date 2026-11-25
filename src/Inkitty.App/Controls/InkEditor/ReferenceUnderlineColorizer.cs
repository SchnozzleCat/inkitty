using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>Underlines the symbol reference under the pointer while the navigation modifier is held.</summary>
public sealed class ReferenceUnderlineColorizer : DocumentColorizingTransformer
{
    private static readonly TextDecorationCollection Underline = [new TextDecoration { Location = TextDecorationLocation.Underline }];

    /// <summary>The segment to underline, or null.</summary>
    public ISegment? Segment { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (Segment is not { } segment || segment.EndOffset <= line.Offset || segment.Offset >= line.EndOffset)
        {
            return;
        }

        var start = Math.Max(segment.Offset, line.Offset);
        var end = Math.Min(segment.EndOffset, line.EndOffset);
        ChangeLinePart(start, end, element => element.TextRunProperties.SetTextDecorations(Underline));
    }
}
