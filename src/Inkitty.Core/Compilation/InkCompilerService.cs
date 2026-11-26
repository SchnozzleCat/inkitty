using System.Diagnostics;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;
using Inkitty.Core.StoryMap;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.Compilation;

public interface IInkCompilerService
{
    CompileResult Compile(ProjectSnapshot snapshot, int generation = 0, CancellationToken cancellationToken = default);
}

/// <summary>
/// Compiles a project snapshot in-process with ink's own compiler. Unsaved buffers are served
/// from memory; the parsed hierarchy is kept to build an exact symbol index, even on failure.
/// </summary>
public sealed class InkCompilerService : IInkCompilerService
{
    /// <summary>
    /// ink's compiler is not thread-safe: static <c>CharacterRange</c>s lazily cache shared
    /// <c>HashSet</c>s. All compiles in the process are serialised through this lock.
    /// </summary>
    private static readonly Lock CompilerLock = new();

    public CompileResult Compile(ProjectSnapshot snapshot, int generation = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (CompilerLock)
        {
            return CompileLocked(snapshot, generation);
        }
    }

    private static CompileResult CompileLocked(ProjectSnapshot snapshot, int generation)
    {
        var stopwatch = Stopwatch.StartNew();

        List<Issue> issues = [];
        string? ResolveFile(string? inkFileName) => snapshot.ResolveFileName(inkFileName);

        var compiler = new Ink.Compiler(snapshot.MainText, new Ink.Compiler.Options
        {
            sourceFilename = snapshot.MainFile,
            countAllVisits = true,
            errorHandler = (message, type) => issues.Add(IssueMessageParser.Parse(message, type, ResolveFile)),
            fileHandler = new ProjectFileHandler(snapshot),
        });

        Ink.Runtime.Story? story;
        try
        {
            story = compiler.Compile();
        }
        catch (Exception ex)
        {
            // The compiler shouldn't throw with an error handler attached, but half-written ink
            // occasionally trips internal asserts. Report it rather than crash the IDE.
            story = null;
            issues.Add(new Issue(IssueSeverity.Error, $"Internal compiler error: {ex.Message}", null, IssueOrigin.Compiler));
        }

        var symbols = compiler.parsedStory is { } parsed
            ? SafeCollect(parsed, ResolveFile, weavePointsResolved: story is not null)
            : SymbolIndex.Empty;

        EnrichColumns(issues, symbols);

        // Some compile failures (e.g. errors in code generation) are reported only via a null story.
        if (story is null && !issues.Any(i => i.Severity.IsError))
        {
            issues.Add(new Issue(IssueSeverity.Error, "Compilation failed.", null, IssueOrigin.Compiler));
        }

        var graph = compiler.parsedStory is { } parsedStory
            ? SafeBuildGraph(parsedStory, snapshot, ResolveFile)
            : StoryGraph.Empty;

        stopwatch.Stop();
        return story is not null
            ? new CompileSucceeded(generation, snapshot, issues, symbols, stopwatch.Elapsed, story) { Graph = graph }
            : new CompileFailed(generation, snapshot, issues, symbols, stopwatch.Elapsed) { Graph = graph };
    }

    private static SymbolIndex SafeCollect(Ink.Parsed.Story parsed, Func<string?, string?> resolveFile, bool weavePointsResolved)
    {
        try
        {
            return ParsedStorySymbolCollector.Collect(parsed, resolveFile, weavePointsResolved);
        }
        catch (Exception)
        {
            return SymbolIndex.Empty;
        }
    }

    private static StoryGraph SafeBuildGraph(Ink.Parsed.Story parsed, ProjectSnapshot snapshot, Func<string?, string?> resolveFile)
    {
        try
        {
            return StoryGraphBuilder.Build(parsed, snapshot.MainFile, resolveFile, snapshot.TextOf);
        }
        catch (Exception)
        {
            return StoryGraph.Empty; // half-parsed hierarchies can throw on missing pieces
        }
    }

    /// <summary>
    /// ink reports errors per line only. Where an unresolved reference sits on that line, narrow the
    /// issue to the reference token so the squiggle is precise.
    /// </summary>
    private static void EnrichColumns(List<Issue> issues, SymbolIndex symbols)
    {
        for (var i = 0; i < issues.Count; i++)
        {
            if (issues[i] is not { Range: { IsWholeLine: true } range } issue || !issue.Severity.IsError)
            {
                continue;
            }

            var candidates = symbols.ReferencesIn(range.File)
                .Where(r => r.Target is null && r.Range.Start.Line == range.Line)
                .ToList();

            var match = candidates.FirstOrDefault(r => issue.Message.Contains(r.Text, StringComparison.Ordinal))
                ?? (candidates.Count == 1 ? candidates[0] : null);

            if (match is not null)
            {
                issues[i] = issue with { Range = match.Range };
            }
        }
    }
}
