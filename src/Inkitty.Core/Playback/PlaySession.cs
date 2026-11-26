using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;

namespace Inkitty.Core.Playback;

/// <summary>
/// Drives one compiled <see cref="Ink.Runtime.Story"/>: continues until choices, makes choices,
/// and converts runtime errors into <see cref="RuntimeIssue"/> events. Not thread-safe; use from one thread at a time.
/// </summary>
public sealed class PlaySession : IDisposable
{
    private readonly Ink.Runtime.Story _story;
    private readonly ProjectSnapshot _snapshot;
    private readonly TimeProvider _time;
    private readonly List<StoryEvent> _pendingIssues = [];

    /// <summary>Compiled stories currently owned by a live session (a Story is single-threaded and stateful).</summary>
    private static readonly HashSet<Ink.Runtime.Story> InUse = [with(ReferenceEqualityComparer.Instance)];

    /// <summary>Cached JSON of compiled stories, used to make independent copies cheaply.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Ink.Runtime.Story, string> JsonCache = [];

    private readonly bool _ownsOriginal;

    public PlaySession(Ink.Runtime.Story story, ProjectSnapshot snapshot, TimeProvider? time = null)
    {
        // Each session needs exclusive use of its Story. Take the compiled one if it's free and
        // resettable; otherwise (another session may be running it on a background thread, or a run
        // was interrupted mid-ContinueAsync) play an independent copy.
        lock (InUse)
        {
            _ownsOriginal = story.asyncContinueComplete && InUse.Add(story);
        }

        _story = _ownsOriginal ? story : CopyOf(story);
        _snapshot = snapshot;
        _time = time ?? TimeProvider.System;
        _story.ResetState(); // always play from the beginning, even when reusing a compiled story
        _story.onError += OnStoryError;
    }

    public ProjectSnapshot Snapshot => _snapshot;

    /// <summary>
    /// An independent copy of a compiled story. The JSON round trip drops ink's debug metadata (source
    /// file and line of each piece of content), so it is copied across from the original's identical tree;
    /// without it, output from the copy can't be traced back to the source.
    /// </summary>
    private static Ink.Runtime.Story CopyOf(Ink.Runtime.Story original)
    {
        var copy = new Ink.Runtime.Story(JsonCache.GetValue(original, s => s.ToJson()));
        CopyDebugMetadata(original.mainContentContainer, copy.mainContentContainer);
        return copy;
    }

    private static void CopyDebugMetadata(Ink.Runtime.Container from, Ink.Runtime.Container to)
    {
        if (from.ownDebugMetadata is { } metadata)
        {
            to.debugMetadata = metadata;
        }

        var count = Math.Min(from.content.Count, to.content.Count);
        for (var i = 0; i < count; i++)
        {
            if (from.content[i] is Ink.Runtime.Container fromChild && to.content[i] is Ink.Runtime.Container toChild)
            {
                CopyDebugMetadata(fromChild, toChild);
            }
            else if (from.content[i].ownDebugMetadata is { } childMetadata)
            {
                to.content[i].debugMetadata = childMetadata;
            }
        }

        // Knots, stitches and labelled weave points that only exist by name (not in the content list).
        foreach (var (name, fromNamed) in from.namedOnlyContent ?? [])
        {
            if (fromNamed is Ink.Runtime.Container fromChild
                && to.namedContent.TryGetValue(name, out var toNamed) && toNamed is Ink.Runtime.Container toChild)
            {
                CopyDebugMetadata(fromChild, toChild);
            }
        }
    }

    public void Dispose()
    {
        _story.onError -= OnStoryError;
        if (_ownsOriginal)
        {
            lock (InUse)
            {
                InUse.Remove(_story);
            }
        }
    }

    public ChoiceHistory History { get; private set; } = ChoiceHistory.Empty;

    public bool HasEnded { get; private set; }

    public IReadOnlyList<ChoiceOption> CurrentChoices =>
        [.. _story.currentChoices.Select(c => new ChoiceOption(c.index, c.text ?? string.Empty, c.tags ?? [], ChoiceLocation(c)))];

    /// <summary>Every global variable's current value, in name order.</summary>
    public IReadOnlyList<StoryVariable> Variables()
    {
        var state = _story.variablesState;
        List<StoryVariable> result = [];
        foreach (var name in state)
        {
            result.Add(StoryVariable.From(name, state.GetVariableWithName(name), state.TryGetDefaultVariableValue(name)));
        }

        return [.. result.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>How many times the knot or stitch at <paramref name="path"/> has been visited (0 if unknown).</summary>
    public int VisitCount(string path)
    {
        try
        {
            return _story.state.VisitCountAtPathString(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Runs the story until it presents choices or ends, returning everything that happened.
    /// The last event is always <see cref="ChoicesOffered"/>, <see cref="StoryEnded"/>, or a <see cref="RuntimeIssue"/>.
    /// </summary>
    public IReadOnlyList<StoryEvent> ContinueUntilChoice(PlaybackBudget budget, CancellationToken cancellationToken = default)
    {
        var start = _time.GetTimestamp();
        List<StoryEvent> events = [];
        Continue(events, budget, start, cancellationToken);
        return events;
    }

    /// <summary>Picks a choice and records it in <see cref="History"/>. Returns the echo event.</summary>
    public ChoiceTaken Choose(int index)
    {
        var choice = _story.currentChoices[index];
        var source = ChoiceLocation(choice);
        History = History.Add(new RecordedChoice(index, RecordedChoice.Fingerprint(choice.text ?? string.Empty)));
        _story.ChooseChoiceIndex(index);
        return new ChoiceTaken(index, choice.text ?? string.Empty, source);
    }

    internal void Continue(List<StoryEvent> events, PlaybackBudget budget, long startTimestamp, CancellationToken cancellationToken)
    {
        var lines = 0;
        while (_story.canContinue)
        {
            // Time-sliced continue so an endless loop can be interrupted.
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                _story.ContinueAsync(budget.SliceMilliseconds);
                if (_time.GetElapsedTime(startTimestamp) > budget.TimeBudget)
                {
                    throw new StoryBudgetExceededException(
                        $"Story appears to loop endlessly near '{CurrentLocation()}' (ran for more than {budget.TimeBudget.TotalSeconds:0.#} s).");
                }
            }
            while (!_story.asyncContinueComplete);

            FlushIssues(events);
            var text = _story.currentText;
            if (!string.IsNullOrWhiteSpace(text) || _story.currentTags.Count > 0)
            {
                events.Add(new TextOutput(text.TrimEnd('\n'), [.. _story.currentTags], OutputLocation()));
            }

            if (++lines > budget.MaxLines)
            {
                throw new StoryBudgetExceededException(
                    $"Story produced more than {budget.MaxLines} lines without a choice near '{CurrentLocation()}'.");
            }

            if (_story.hasError)
            {
                break;
            }
        }

        FlushIssues(events);

        var choices = CurrentChoices;
        if (choices.Count > 0)
        {
            events.Add(new ChoicesOffered(choices));
        }
        else
        {
            HasEnded = true;
            events.Add(new StoryEnded());
        }
    }

    internal void RecordChoiceWithoutEcho(int index, RecordedChoice recorded)
    {
        History = History.Add(recorded with { Index = index });
        _story.ChooseChoiceIndex(index);
    }

    private string CurrentLocation()
    {
        try
        {
            return _story.state.currentPathString ?? _story.state.previousPathString ?? "?";
        }
        catch (Exception)
        {
            return "?";
        }
    }

    /// <summary>
    /// Where the line just produced was written: the first piece of visible text in the output with its
    /// own source position (leading whitespace often comes from the choice that led here).
    /// </summary>
    private StoryLocation? OutputLocation()
    {
        Ink.Runtime.Object? best = null;
        foreach (var obj in _story.state.outputStream)
        {
            if (obj.ownDebugMetadata is null)
            {
                continue;
            }

            if (obj is Ink.Runtime.StringValue { value: var value } && !string.IsNullOrWhiteSpace(value))
            {
                best = obj;
                break;
            }

            best ??= obj;
        }

        return best is null ? null : LocationOf(best.ownDebugMetadata, best.parent?.path?.ToString());
    }

    private StoryLocation? ChoiceLocation(Ink.Runtime.Choice choice)
    {
        try
        {
            var content = _story.ContentAtPath(new Ink.Runtime.Path(choice.sourcePath)).obj;
            return LocationOf(content?.debugMetadata, choice.sourcePath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private StoryLocation? LocationOf(Ink.Runtime.DebugMetadata? metadata, string? runtimePath) =>
        metadata is { startLineNumber: > 0 }
            ? new StoryLocation(_snapshot.ResolveFileName(metadata.fileName), metadata.startLineNumber, runtimePath)
            : null;

    private void OnStoryError(string message, Ink.ErrorType type) =>
        _pendingIssues.Add(new RuntimeIssue(IssueMessageParser.Parse(message, type, _snapshot.ResolveFileName, isRuntime: true)));

    private void FlushIssues(List<StoryEvent> events)
    {
        events.AddRange(_pendingIssues);
        _pendingIssues.Clear();
    }
}
