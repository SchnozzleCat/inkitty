using Inkitty.Core.Compilation;
using Inkitty.Core.Playback;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.Playback;

public class StoryLocationTests
{
    private const string Main = """
        INCLUDE forest.ink
        Hello.
        * [Into the forest] -> forest
        """;

    private const string Forest = """
        === forest ===
        Trees.
        -> deeper
        = deeper
        Darker.
        * [Back] -> END
        """;

    private static PlaySession Session()
    {
        var snapshot = InMemoryProject.Snapshot(("main.ink", Main), ("forest.ink", Forest));
        return new InkCompilerService().Compile(snapshot) is CompileSucceeded ok
            ? new PlaySession(ok.Story, snapshot)
            : throw new InvalidOperationException("compile failed");
    }

    [Test]
    public async Task Text_and_choices_carry_their_source_location()
    {
        using var session = Session();
        var events = session.ContinueUntilChoice(PlaybackBudget.Default);

        var hello = events.OfCase<TextOutput>().Single();
        await Assert.That(hello.Source).IsEqualTo(new StoryLocation("main.ink", 2, hello.Source!.RuntimePath));

        var choice = events.OfCase<ChoicesOffered>().Single().Choices.Single();
        await Assert.That(choice.Source?.File).IsEqualTo("main.ink");
        await Assert.That(choice.Source?.Line).IsEqualTo(3);

        var taken = session.Choose(0);
        await Assert.That(taken.Source?.Line).IsEqualTo(3);

        var texts = session.ContinueUntilChoice(PlaybackBudget.Default).OfCase<TextOutput>().ToList();
        await Assert.That(texts.Select(t => (t.Text, t.Source?.File, t.Source?.Line)))
            .IsEquivalentTo([("Trees.", (string?)"forest.ink", (int?)2), ("Darker.", "forest.ink", 5)]);
        await Assert.That(texts[1].Source?.RuntimePath).StartsWith("forest.deeper");
    }

    [Test]
    public async Task Visit_counts_are_reported_per_knot_and_stitch()
    {
        using var session = Session();
        session.ContinueUntilChoice(PlaybackBudget.Default);
        await Assert.That(session.VisitCount("forest")).IsEqualTo(0);

        session.Choose(0);
        session.ContinueUntilChoice(PlaybackBudget.Default);

        await Assert.That(session.VisitCount("forest")).IsEqualTo(1);
        await Assert.That(session.VisitCount("forest.deeper")).IsEqualTo(1);
        await Assert.That(session.VisitCount("no_such_knot")).IsEqualTo(0);
    }
}
