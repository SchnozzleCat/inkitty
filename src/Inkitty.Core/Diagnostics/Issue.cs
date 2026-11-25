using Inkitty.Core.Symbols;

namespace Inkitty.Core.Diagnostics;

public enum IssueOrigin
{
    Compiler,
    Runtime,
    Project,
}

/// <summary>An error, warning or TODO. <see cref="Range"/> is null if ink reported no location.</summary>
public sealed record Issue(IssueSeverity Severity, string Message, SourceRange? Range, IssueOrigin Origin)
{
    public string? File => Range?.File;

    public int? Line => Range?.Line;

    public override string ToString() => Range is { } r ? $"{Severity} {r}: {Message}" : $"{Severity}: {Message}";
}
