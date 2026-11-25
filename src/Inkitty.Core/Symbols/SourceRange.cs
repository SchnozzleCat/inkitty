namespace Inkitty.Core.Symbols;

/// <summary>A 1-based line/column position.</summary>
public readonly record struct SourcePosition(int Line, int Column) : IComparable<SourcePosition>
{
    public int CompareTo(SourcePosition other) =>
        Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(SourcePosition a, SourcePosition b) => a.CompareTo(b) < 0;

    public static bool operator >(SourcePosition a, SourcePosition b) => a.CompareTo(b) > 0;

    public static bool operator <=(SourcePosition a, SourcePosition b) => a.CompareTo(b) <= 0;

    public static bool operator >=(SourcePosition a, SourcePosition b) => a.CompareTo(b) >= 0;
}

/// <summary>
/// A range in one project file. Lines and columns are 1-based; <see cref="End"/> is exclusive.
/// A range with <see cref="IsWholeLine"/> covers the trimmed contents of <see cref="Start"/>'s line.
/// </summary>
public readonly record struct SourceRange(string File, SourcePosition Start, SourcePosition End)
{
    public static SourceRange WholeLine(string file, int line) => new(file, new(line, 0), new(line, 0));

    public bool IsWholeLine => Start.Column == 0;

    public int Line => Start.Line;

    public bool Contains(SourcePosition position) =>
        IsWholeLine ? position.Line == Start.Line : position >= Start && position < End;

    public override string ToString() =>
        IsWholeLine ? $"{File}:{Start.Line}" : $"{File}:{Start.Line}:{Start.Column}";
}

public static class DebugMetadataExtensions
{
    extension(Ink.Runtime.DebugMetadata metadata)
    {
        public SourcePosition StartPosition => new(metadata.startLineNumber, metadata.startCharacterNumber);

        public SourcePosition EndPosition => new(metadata.endLineNumber, metadata.endCharacterNumber);

        /// <summary>Converts ink's metadata to a range in the given (already resolved) project file.</summary>
        public SourceRange ToRange(string file) => new(file, metadata.StartPosition, metadata.EndPosition);
    }
}
