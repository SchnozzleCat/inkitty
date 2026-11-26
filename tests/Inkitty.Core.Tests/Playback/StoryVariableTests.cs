using Inkitty.Core.Compilation;
using Inkitty.Core.Playback;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.Playback;

public class StoryVariableTests
{
    private const string Story = """
        VAR coins = 3
        VAR price = 1.5
        VAR name = "Ada"
        VAR lit = false
        VAR next = -> shop
        LIST mood = calm, (nervous), angry
        CONST LIMIT = 10
        Start.
        * [Shop] -> shop
        === shop ===
        ~ coins = coins - 1
        ~ lit = true
        ~ mood = angry
        ~ temp local = 5
        Bought.
        * [Again] -> shop
        """;

    private static PlaySession Session()
    {
        var snapshot = InMemoryProject.OneFile(Story);
        return new InkCompilerService().Compile(snapshot) is CompileSucceeded ok
            ? new PlaySession(ok.Story, snapshot)
            : throw new InvalidOperationException("compile failed");
    }

    private static Dictionary<string, StoryVariable> ByName(PlaySession session) =>
        session.Variables().ToDictionary(v => v.Name);

    [Test]
    public async Task Globals_are_listed_with_ink_formatted_values()
    {
        using var session = Session();
        session.ContinueUntilChoice(PlaybackBudget.Default);
        var vars = ByName(session);

        // Constants are inlined by the compiler and temporaries aren't global, so neither is listed.
        await Assert.That(vars.Keys).IsEquivalentTo(["coins", "lit", "mood", "name", "next", "price"]);
        await Assert.That(vars["coins"].Value).IsEqualTo("3");
        await Assert.That(vars["coins"].Kind).IsEqualTo(StoryVariableKind.WholeNumber);
        await Assert.That(vars["price"].Value).IsEqualTo("1.5");
        await Assert.That(vars["name"].Value).IsEqualTo("\"Ada\"");
        await Assert.That(vars["lit"].Value).IsEqualTo("false");
        await Assert.That(vars["next"].Value).IsEqualTo("-> shop");
        await Assert.That(vars["mood"].Value).IsEqualTo("nervous");
        await Assert.That(session.Variables().Any(v => v.IsChanged)).IsFalse();
    }

    [Test]
    public async Task Values_follow_the_story_and_remember_their_start()
    {
        using var session = Session();
        session.ContinueUntilChoice(PlaybackBudget.Default);
        session.Choose(0);
        session.ContinueUntilChoice(PlaybackBudget.Default);
        session.Choose(0);
        session.ContinueUntilChoice(PlaybackBudget.Default);
        var vars = ByName(session);

        await Assert.That(vars["coins"].Value).IsEqualTo("1");
        await Assert.That(vars["coins"].DefaultValue).IsEqualTo("3");
        await Assert.That(vars["coins"].IsChanged).IsTrue();
        await Assert.That(vars["lit"].Value).IsEqualTo("true");
        await Assert.That(vars["mood"].Value).IsEqualTo("angry");
        await Assert.That(vars["name"].IsChanged).IsFalse();
    }
}
