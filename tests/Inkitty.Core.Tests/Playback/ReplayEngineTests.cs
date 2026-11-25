using Inkitty.Core.Compilation;
using Inkitty.Core.Playback;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.Playback;

public class ReplayEngineTests
{
    private const string Story = """
        Start.
        * [Go left] Left.
            * * [Deeper] Deep left.
                -> END
        * [Go right] Right.
            -> END
        """;

    private static PlaySession Session(string ink)
    {
        var snapshot = InMemoryProject.OneFile(ink);
        var result = new InkCompilerService().Compile(snapshot);
        return result is CompileSucceeded ok
            ? new PlaySession(ok.Story, snapshot)
            : throw new InvalidOperationException(string.Join('\n', result.Issues));
    }

    private static IEnumerable<string> Texts(IEnumerable<StoryEvent> events) =>
        events.Select(e => e switch
        {
            TextOutput t => t.Text,
            ChoiceTaken c => "> " + c.Text,
            ChoicesOffered o => "[" + string.Join("|", o.Choices.Select(c => c.Text)) + "]",
            StoryEnded => "END",
            RuntimeIssue i => "!" + i.Issue.Message,
        });

    private static ChoiceHistory Play(string ink, params int[] choices)
    {
        var session = Session(ink);
        session.ContinueUntilChoice(PlaybackBudget.Default);
        foreach (var choice in choices)
        {
            session.Choose(choice);
            session.ContinueUntilChoice(PlaybackBudget.Default);
        }

        return session.History;
    }

    [Test]
    public async Task Plays_until_choices()
    {
        var events = Session(Story).ContinueUntilChoice(PlaybackBudget.Default);

        await Assert.That(Texts(events)).IsEquivalentTo(["Start.", "[Go left|Go right]"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Text_edit_keeps_position()
    {
        var history = Play(Story, 0);

        var edited = Story.Replace("Left.", "Left, edited.", StringComparison.Ordinal);
        var result = ReplayEngine.Replay(Session(edited), history, PlaybackBudget.Default);

        await Assert.That(result.Diverged).IsFalse();
        await Assert.That(result.ReplayedChoices).IsEqualTo(1);
        await Assert.That(Texts(result.Events)).IsEquivalentTo(
            ["Start.", "> Go left", "Left, edited.", "[Deeper]"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Inserted_choice_above_is_skipped_by_fingerprint()
    {
        var history = Play(Story, 1); // "Go right"

        var edited = Story.Replace("* [Go left]", "* [Go up] Up.\n    -> END\n* [Go left]", StringComparison.Ordinal);
        var result = ReplayEngine.Replay(Session(edited), history, PlaybackBudget.Default);

        await Assert.That(result.Diverged).IsFalse();
        await Assert.That(Texts(result.Events)).Contains("> Go right");
        await Assert.That(result.Session.History.Choices[0].Index).IsEqualTo(2);
    }

    [Test]
    public async Task Removed_choice_truncates_history()
    {
        var history = Play(Story, 0, 0);

        var edited = Story.Replace("* * [Deeper] Deep left.", "* * [Somewhere else] Else.", StringComparison.Ordinal);
        var result = ReplayEngine.Replay(Session(edited), history, PlaybackBudget.Default);

        await Assert.That(result.Diverged).IsTrue();
        await Assert.That(result.ReplayedChoices).IsEqualTo(1);
        await Assert.That(result.Events[^1].Is<ChoicesOffered>()).IsTrue();
    }

    [Test]
    public async Task Endless_loop_is_stopped_within_budget()
    {
        const string loop = "-> a\n=== a ===\n~ temp x = 1\n-> b\n=== b ===\n-> a";
        var session = Session(loop);

        var started = DateTime.UtcNow;
        var result = ReplayEngine.Replay(session, ChoiceHistory.Empty, new PlaybackBudget(TimeSpan.FromMilliseconds(300)));

        await Assert.That(DateTime.UtcNow - started).IsLessThan(TimeSpan.FromSeconds(5));
        await Assert.That(result.Events.OfCase<RuntimeIssue>().Single().Issue.Message).Contains("loop");
    }

    [Test]
    public async Task Story_interrupted_by_the_guard_can_be_replayed_again()
    {
        const string loop = "* [Loop] -> a\n* [Stop] Stopped.\n-> END\n=== a ===\n~ temp x = 1\n-> b\n=== b ===\n-> a";
        var snapshot = InMemoryProject.OneFile(loop);
        var story = ((CompileSucceeded)new InkCompilerService().Compile(snapshot)).Story;

        var first = new PlaySession(story, snapshot);
        first.ContinueUntilChoice(PlaybackBudget.Default);
        first.Choose(0);
        var stopped = ReplayEngine.Replay(new PlaySession(story, snapshot), first.History, new PlaybackBudget(TimeSpan.FromMilliseconds(200)));
        await Assert.That(stopped.Events.OfCase<RuntimeIssue>()).IsNotEmpty();

        // Restart on the same compiled story must still work.
        var again = new PlaySession(story, snapshot).ContinueUntilChoice(PlaybackBudget.Default);
        await Assert.That(again[^1].Is<ChoicesOffered>()).IsTrue();
    }

    [Test]
    public async Task Text_flood_is_stopped_by_line_budget()
    {
        const string flood = "-> a\n=== a ===\nLine\n-> a";
        var result = ReplayEngine.Replay(Session(flood), ChoiceHistory.Empty, new PlaybackBudget(TimeSpan.FromSeconds(10), MaxLines: 100));

        await Assert.That(result.Events.OfCase<RuntimeIssue>()).IsNotEmpty();
        await Assert.That(result.Events.OfCase<TextOutput>().Count()).IsLessThanOrEqualTo(101);
    }

    [Test]
    public async Task Runtime_errors_are_reported_as_issues()
    {
        // Running out of content without -> END/DONE inside a knot is a runtime error.
        const string ink = "-> a\n=== a ===\nHello\n";
        var events = Session(ink).ContinueUntilChoice(PlaybackBudget.Default);

        await Assert.That(events.OfCase<RuntimeIssue>().Any()).IsTrue();
    }

    [Test]
    public async Task Tags_are_captured()
    {
        var events = Session("Hello # mood: happy # loud\n-> END").ContinueUntilChoice(PlaybackBudget.Default);

        var text = events.OfCase<TextOutput>().Single();
        await Assert.That(text.Tags).IsEquivalentTo(["mood: happy", "loud"]);
    }
}
