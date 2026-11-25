using System.Collections.Immutable;

namespace Inkitty.Core.Playback;

/// <summary>
/// One choice the player made. The fingerprint lets replay follow the same choice even if
/// choices were inserted or reordered by an edit.
/// </summary>
public readonly record struct RecordedChoice(int Index, string TextFingerprint)
{
    public static string Fingerprint(string choiceText) =>
        string.Join(' ', choiceText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    public static RecordedChoice From(ChoiceOption choice) => new(choice.Index, Fingerprint(choice.Text));
}

/// <summary>The path the player took through the story, replayed after every recompile.</summary>
public sealed record ChoiceHistory(ImmutableList<RecordedChoice> Choices)
{
    public static ChoiceHistory Empty { get; } = new(ImmutableList<RecordedChoice>.Empty);

    public int Count => Choices.Count;

    public ChoiceHistory Add(RecordedChoice choice) => new(Choices.Add(choice));

    public ChoiceHistory Truncate(int count) =>
        count >= Choices.Count ? this : new(Choices.RemoveRange(count, Choices.Count - count));

    public ChoiceHistory RemoveLast() => Choices.IsEmpty ? this : Truncate(Choices.Count - 1);
}
