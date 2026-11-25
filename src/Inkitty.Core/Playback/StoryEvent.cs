using Inkitty.Core.Diagnostics;

namespace Inkitty.Core.Playback;

/// <summary>A line of story text, with its tags.</summary>
public sealed record TextOutput(string Text, IReadOnlyList<string> Tags);

/// <summary>A choice available to the player.</summary>
public sealed record ChoiceOption(int Index, string Text, IReadOnlyList<string> Tags);

/// <summary>The story is waiting for the player to pick one of these.</summary>
public sealed record ChoicesOffered(IReadOnlyList<ChoiceOption> Choices);

/// <summary>Echo of the choice the player (or replay) picked.</summary>
public sealed record ChoiceTaken(int Index, string Text);

/// <summary>The story reached its end (no more content and no choices).</summary>
public sealed record StoryEnded;

/// <summary>An error or warning raised by the ink runtime.</summary>
public sealed record RuntimeIssue(Issue Issue);

/// <summary>Everything the player pane can display.</summary>
public union StoryEvent(TextOutput, ChoicesOffered, ChoiceTaken, StoryEnded, RuntimeIssue);

public static class StoryEventExtensions
{
    extension(IEnumerable<StoryEvent> events)
    {
        /// <summary>The events whose case is <typeparamref name="T"/> (unions wrap their case, so <c>OfType</c> doesn't work).</summary>
        public IEnumerable<T> OfCase<T>()
            where T : class => events.Select(e => e.Value).OfType<T>();
    }

    extension(StoryEvent storyEvent)
    {
        public bool Is<T>()
            where T : class => storyEvent.Value is T;
    }
}
