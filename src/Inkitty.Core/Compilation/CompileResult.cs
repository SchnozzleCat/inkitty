using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.Compilation;

/// <summary>The outcome of one compilation. Exactly one of the two cases below.</summary>
public closed record CompileResult(int Generation, ProjectSnapshot Snapshot, IReadOnlyList<Issue> Issues, SymbolIndex Symbols, TimeSpan Duration)
{
    public bool HasErrors => Issues.Any(i => i.Severity.IsError);
}

/// <summary>Compiled successfully (possibly with warnings); <see cref="Story"/> is ready to play.</summary>
public sealed record CompileSucceeded(int Generation, ProjectSnapshot Snapshot, IReadOnlyList<Issue> Issues, SymbolIndex Symbols, TimeSpan Duration, Ink.Runtime.Story Story)
    : CompileResult(Generation, Snapshot, Issues, Symbols, Duration);

/// <summary>Compilation failed; <see cref="CompileResult.Issues"/> explains why.</summary>
public sealed record CompileFailed(int Generation, ProjectSnapshot Snapshot, IReadOnlyList<Issue> Issues, SymbolIndex Symbols, TimeSpan Duration)
    : CompileResult(Generation, Snapshot, Issues, Symbols, Duration);
