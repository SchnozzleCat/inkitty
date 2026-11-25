namespace Inkitty.Core.Playback;

/// <summary>Limits that stop runaway stories (e.g. an endless divert loop) from hanging the IDE.</summary>
/// <param name="TimeBudget">Total wall-clock time allowed for one run (a replay or a single turn).</param>
/// <param name="MaxLines">Maximum number of lines of output for one run.</param>
/// <param name="SliceMilliseconds">Time slice passed to <c>Story.ContinueAsync</c>.</param>
public sealed record PlaybackBudget(TimeSpan TimeBudget, int MaxLines = 10_000, float SliceMilliseconds = 20)
{
    public static PlaybackBudget Default { get; } = new(TimeSpan.FromSeconds(2));
}

/// <summary>Thrown when a run exceeds its <see cref="PlaybackBudget"/>.</summary>
public sealed class StoryBudgetExceededException(string message) : Exception(message);
