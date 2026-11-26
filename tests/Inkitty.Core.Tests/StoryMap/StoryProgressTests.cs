using Inkitty.Core.Compilation;
using Inkitty.Core.Playback;
using Inkitty.Core.StoryMap;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.StoryMap;

public class StoryProgressTests
{
    // Mirrors samples/multi-file: "town" only diverts to its stitch, so it never outputs anything itself.
    private const string Story = """
        Crossroads.
        * [Forest] -> forest
        * [Town] -> town
        === forest ===
        Dark trees.
        * [Onwards] -> town
        === town ===
        -> square
        = square
        Busy square.
        * [Inn] Warm. -> END
        * [Back] -> forest
        """;

    private static (StoryProgress Progress, StoryGraph Graph) Play(params int[] choices)
    {
        var snapshot = InMemoryProject.OneFile(Story);
        var compiled = (CompileSucceeded)new InkCompilerService().Compile(snapshot);
        using var session = new PlaySession(compiled.Story, snapshot);
        List<StoryEvent> events = [.. session.ContinueUntilChoice(PlaybackBudget.Default)];
        foreach (var choice in choices)
        {
            events.Add(session.Choose(choice));
            events.AddRange(session.ContinueUntilChoice(PlaybackBudget.Default));
        }

        return (StoryProgress.Build(compiled.Graph, events, session.VisitCount), compiled.Graph);
    }

    [Test]
    public async Task Fresh_story_is_at_the_start()
    {
        var (progress, _) = Play();

        await Assert.That(progress.Path).IsEquivalentTo([StoryGraph.StartId]);
        await Assert.That(progress.CurrentNode).IsEqualTo(StoryGraph.StartId);
        await Assert.That(progress.Current?.Line).IsEqualTo(1);
        await Assert.That(progress.OfferedChoices.Select(c => c.Line)).IsEquivalentTo([2, 3]);
    }

    [Test]
    public async Task Path_follows_choices_and_fills_in_silent_knots()
    {
        var (progress, graph) = Play(0, 0); // forest, then onwards to town (which diverts straight to its square)

        await Assert.That(progress.Path).IsEquivalentTo([StoryGraph.StartId, "forest", "town", "town.square"]);
        await Assert.That(progress.CurrentNode).IsEqualTo("town.square");
        await Assert.That(progress.WasVisited("town")).IsTrue();
        await Assert.That(progress.TookEdge("forest", "town")).IsTrue();
        await Assert.That(progress.TookEdge(StoryGraph.StartId, "town")).IsFalse();
        foreach (var (from, to) in progress.Path.Zip(progress.Path.Skip(1)))
        {
            await Assert.That(graph.HasEdge(from, to)).IsTrue();
        }
    }

    [Test]
    public async Task Revisits_are_counted_and_the_end_is_reached()
    {
        var (progress, _) = Play(1, 1, 0, 0); // town, back to forest, onwards to town, inn ("Back" is once-only)

        await Assert.That(progress.VisitCount("town.square")).IsEqualTo(2);
        await Assert.That(progress.VisitCount("forest")).IsEqualTo(1);
        await Assert.That(progress.HasEnded).IsTrue();
        await Assert.That(progress.CurrentNode).IsEqualTo(StoryGraph.EndId);
        await Assert.That(progress.OfferedChoices).IsEmpty();
    }

    [Test]
    public async Task File_progress_marks_visited_current_and_choice_lines()
    {
        var (progress, _) = Play(0);
        var file = progress.ForFile("main.ink");

        await Assert.That(file.VisitedLines).IsEquivalentTo([1, 2, 5]); // crossroads, the chosen choice, dark trees
        await Assert.That(file.CurrentLine).IsEqualTo(5);
        await Assert.That(file.ChoiceLines).IsEquivalentTo([6]);
        await Assert.That(progress.ForFile("other.ink").IsEmpty).IsTrue();
    }
}
