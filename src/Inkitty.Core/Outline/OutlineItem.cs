using Inkitty.Core.Symbols;

namespace Inkitty.Core.Outline;

public enum OutlineKind
{
    Include,
    Variable,
    Constant,
    List,
    External,
    Knot,
    Stitch,
    Function,
    Choice,
    StickyChoice,
    Gather,
}

/// <summary>One element of a file's outline, with the elements nested inside it.</summary>
/// <param name="Name">What to show: a knot name, a choice's text, a variable name.</param>
/// <param name="Detail">Secondary text after the name: parameters, a variable's value.</param>
/// <param name="Range">Where to jump to (the name, or the whole line).</param>
/// <param name="Condition">A choice's conditions as written, e.g. <c>{has_torch} {coins &gt; 1}</c>; shown before its text.</param>
/// <param name="Label">A choice's label as written, e.g. <c>(shouted)</c>; shown first, as in the source.</param>
public sealed record OutlineItem(OutlineKind Kind, string Name, string? Detail, SourceRange Range, IReadOnlyList<OutlineItem> Children, string? Condition = null, string? Label = null)
{
    public int Line => Range.Line;

    /// <summary>Stable across edits that don't rename or reorder (used to keep tree state).</summary>
    public string Key { get; init; } = string.Empty;

    public bool IsFlow => Kind is OutlineKind.Knot or OutlineKind.Stitch or OutlineKind.Function;

    public bool IsWeave => Kind is OutlineKind.Choice or OutlineKind.StickyChoice or OutlineKind.Gather;
}
