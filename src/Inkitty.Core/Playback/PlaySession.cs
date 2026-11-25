using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;

namespace Inkitty.Core.Playback;

/// <summary>
/// Drives one compiled <see cref="Ink.Runtime.Story"/>: continues until choices, makes choices,
/// and converts runtime errors into <see cref="RuntimeIssue"/> events. Not thread-safe; use from one thread at a time.
/// </summary>
public sealed class PlaySession
{
    private readonly Ink.Runtime.Story _story;
    private readonly ProjectSnapshot _snapshot;
    private readonly TimeProvider _time;
    private readonly List<StoryEvent> _pendingIssues = [];

    public PlaySession(Ink.Runtime.Story story, ProjectSnapshot snapshot, TimeProvider? time = null)
    {
        _story = story;
        _snapshot = snapshot;
        _time = time ?? TimeProvider.System;
        _story.onError += OnStoryError;
    }

    public ChoiceHistory History { get; private set; } = ChoiceHistory.Empty;

    public bool HasEnded { get; private set; }

    public IReadOnlyList<ChoiceOption> CurrentChoices =>
        [.. _story.currentChoices.Select(c => new ChoiceOption(c.index, c.text ?? string.Empty, c.tags ?? []))];

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
        History = History.Add(new RecordedChoice(index, RecordedChoice.Fingerprint(choice.text ?? string.Empty)));
        _story.ChooseChoiceIndex(index);
        return new ChoiceTaken(index, choice.text ?? string.Empty);
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
                events.Add(new TextOutput(text.TrimEnd('\n'), [.. _story.currentTags]));
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

    private void OnStoryError(string message, Ink.ErrorType type) =>
        _pendingIssues.Add(new RuntimeIssue(IssueMessageParser.Parse(message, type, _snapshot.ResolveFileName, isRuntime: true)));

    private void FlushIssues(List<StoryEvent> events)
    {
        events.AddRange(_pendingIssues);
        _pendingIssues.Clear();
    }
}
