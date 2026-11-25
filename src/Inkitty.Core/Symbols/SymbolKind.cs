namespace Inkitty.Core.Symbols;

public enum SymbolKind
{
    Knot,
    Stitch,
    Function,
    Label,
    Variable,
    Temporary,
    Parameter,
    Constant,
    List,
    ListItem,
    External,
}

/// <summary>A place where a name is defined.</summary>
/// <param name="Name">The simple name, e.g. <c>clearing</c>.</param>
/// <param name="QualifiedName">Dotted name, e.g. <c>forest.clearing</c>.</param>
/// <param name="Range">Range of the name token itself.</param>
/// <param name="Container">Qualified name of the enclosing knot/stitch, if any.</param>
public sealed record SymbolDefinition(string Name, string QualifiedName, SymbolKind Kind, SourceRange Range, string? Container = null)
{
    public override string ToString() => $"{Kind} {QualifiedName} @ {Range}";
}

/// <summary>A use of a name (a divert target component, variable or function call).</summary>
/// <param name="Text">The referencing text (one path component, or a whole variable name).</param>
/// <param name="Target">The resolved definition, or null if it couldn't be resolved.</param>
public sealed record SymbolReference(string Text, SourceRange Range, SymbolDefinition? Target);
