using Inkitty.Core.Diagnostics;

namespace Inkitty.Core.Playback;

/// <summary>Result of fast-forwarding a fresh story along a recorded choice history.</summary>
/// <param name="Events">Everything that was output, including echoes of replayed choices.</param>
/// <param name="ReplayedChoices">How many recorded choices could be followed.</param>
/// <param name="Diverged">True if the history couldn't be followed to the end (it was truncated).</param>
public sealed record ReplayResult(PlaySession Session, IReadOnlyList<StoryEvent> Events, int ReplayedChoices, bool Diverged);

/// <summary>
/// Implements "play as you write": after a recompile, a fresh story is driven along the
/// previous choice path so the player stays where it was.
/// </summary>
public static class ReplayEngine
{
    public static ReplayResult Replay(PlaySession session, ChoiceHistory history, PlaybackBudget budget, TimeProvider? time = null, CancellationToken cancellationToken = default)
    {
        time ??= TimeProvider.System;
        var start = time.GetTimestamp();
        List<StoryEvent> events = [];
        var replayed = 0;
        var diverged = false;

        try
        {
            foreach (var recorded in history.Choices)
            {
                session.Continue(events, budget, start, cancellationToken);
                if (events[^1] is not ChoicesOffered { Choices: var choices })
                {
                    diverged = true; // story ends (or errors) before this choice now
                    break;
                }

                var index = MatchChoice(choices, recorded);
                if (index < 0)
                {
                    diverged = true;
                    break;
                }

                // Replace the offered choices with the echo of the one taken.
                events.RemoveAt(events.Count - 1);
                events.Add(new ChoiceTaken(index, choices[index].Text));
                session.RecordChoiceWithoutEcho(index, recorded);
                replayed++;
            }

            if (!diverged)
            {
                session.Continue(events, budget, start, cancellationToken);
            }
        }
        catch (StoryBudgetExceededException ex)
        {
            events.Add(new RuntimeIssue(new Issue(IssueSeverity.RuntimeError, ex.Message, null, IssueOrigin.Runtime)));
            events.Add(new StoryEnded());
            diverged = true;
        }
        catch (Exception ex) when (ex is Ink.Runtime.StoryException or InvalidOperationException or ArgumentException)
        {
            events.Add(new RuntimeIssue(new Issue(IssueSeverity.RuntimeError, ex.Message, null, IssueOrigin.Runtime)));
            events.Add(new StoryEnded());
            diverged = true;
        }

        return new ReplayResult(session, events, replayed, diverged);
    }

    /// <summary>
    /// Prefer the same index if the text still matches; otherwise find the choice with the same text
    /// (tolerates inserted/reordered choices). Returns -1 if the choice no longer exists.
    /// </summary>
    internal static int MatchChoice(IReadOnlyList<ChoiceOption> choices, RecordedChoice recorded)
    {
        if (recorded.Index < choices.Count && RecordedChoice.Fingerprint(choices[recorded.Index].Text) == recorded.TextFingerprint)
        {
            return recorded.Index;
        }

        for (var i = 0; i < choices.Count; i++)
        {
            if (RecordedChoice.Fingerprint(choices[i].Text) == recorded.TextFingerprint)
            {
                return i;
            }
        }

        return -1;
    }
}
