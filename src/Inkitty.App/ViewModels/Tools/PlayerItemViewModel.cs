using Inkitty.Core.Diagnostics;
using Inkitty.Core.Playback;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>One row in the player transcript.</summary>
public abstract record PlayerItem
{
    /// <summary>True for content produced after the last replayed choice (highlighted briefly).</summary>
    public bool IsNew { get; init; }
}

/// <param name="Source">Where the text was written, for "reveal in editor".</param>
public sealed record PlayerParagraph(string Text, IReadOnlyList<string> Tags, StoryLocation? Source = null) : PlayerItem
{
    public bool HasTags => Tags.Count > 0;

    public bool HasSource => Source?.File is not null;

    public string SourceText => Source is { File: { } file, Line: var line } ? $"{file}:{line}" : string.Empty;
}

public sealed record PlayerChoiceEcho(string Text, StoryLocation? Source = null) : PlayerItem
{
    public bool HasSource => Source?.File is not null;

    public string SourceText => Source is { File: { } file, Line: var line } ? $"{file}:{line}" : string.Empty;
}

public sealed record PlayerTurnDivider : PlayerItem;

public sealed record PlayerEnd : PlayerItem;

public sealed record PlayerError(string Message, bool IsWarning) : PlayerItem;

/// <summary>A choice button.</summary>
public sealed record PlayerChoice(int Index, string Text);

internal static class PlayerItemMapper
{
    /// <summary>Converts story events into transcript rows. Choices offered are returned separately.</summary>
    public static (List<PlayerItem> Items, IReadOnlyList<PlayerChoice> Choices) Map(IEnumerable<StoryEvent> events, int newFromIndex = int.MaxValue)
    {
        List<PlayerItem> items = [];
        IReadOnlyList<PlayerChoice> choices = [];
        var index = 0;
        foreach (var storyEvent in events)
        {
            var isNew = index++ >= newFromIndex;
            switch (storyEvent)
            {
                case TextOutput text:
                    items.Add(new PlayerParagraph(text.Text, text.Tags, text.Source) { IsNew = isNew });
                    break;
                case ChoiceTaken taken:
                    items.Add(new PlayerChoiceEcho(taken.Text, taken.Source) { IsNew = isNew });
                    items.Add(new PlayerTurnDivider { IsNew = isNew });
                    break;
                case ChoicesOffered offered:
                    choices = [.. offered.Choices.Select(c => new PlayerChoice(c.Index, c.Text))];
                    break;
                case StoryEnded:
                    items.Add(new PlayerEnd { IsNew = isNew });
                    break;
                case RuntimeIssue issue:
                    items.Add(new PlayerError(issue.Issue.Message, issue.Issue.Severity.IsWarning) { IsNew = isNew });
                    break;
            }
        }

        return (items, choices);
    }
}
