using Inkitty.Core.Compilation;
using Inkitty.Core.StoryMap;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.StoryMap;

public class StoryGraphBuilderTests
{
    private static StoryGraph Graph(params (string Path, string Text)[] files) =>
        new InkCompilerService().Compile(InMemoryProject.Snapshot(files)).Graph;

    private static string[] EdgeList(StoryGraph graph) =>
        [.. graph.Edges.Select(e => e.Label is null ? $"{e.From}->{e.To}:{e.Kind}" : $"{e.From}->{e.To}:{e.Kind}:{e.Label}").Order()];

    [Test]
    public async Task Knots_stitches_and_diverts_become_nodes_and_edges()
    {
        var graph = Graph(("main.ink", """
            -> forest
            === forest ===
            = clearing
            Trees. -> town
            === town ===
            Done. -> END
            """));

        await Assert.That(graph.Nodes.Select(n => n.Id)).IsEquivalentTo(["(start)", "forest", "forest.clearing", "town", "(end)"]);
        await Assert.That(EdgeList(graph)).IsEquivalentTo([
            "(start)->forest:Divert",
            "forest->forest.clearing:Auto",
            "forest.clearing->town:Divert",
            "town->(end):Divert",
        ]);
        await Assert.That(graph.Find("forest.clearing")!.Parent).IsEqualTo("forest");
        await Assert.That(graph.Find("town")!.Range!.Value.Line).IsEqualTo(5);
    }

    [Test]
    public async Task Choice_diverts_are_labelled_with_the_choice_text()
    {
        var graph = Graph(("main.ink", """
            -> hub
            === hub ===
            * [Go left] -> left
            * Go right[.] now
                -> right
            * (named) {true} Third -> hub
            === left ===
            -> END
            === right ===
            -> END
            """));

        await Assert.That(EdgeList(graph)).Contains("hub->left:Choice:Go left");
        await Assert.That(EdgeList(graph)).Contains("hub->right:Choice:Go right.");
        await Assert.That(EdgeList(graph)).Contains("hub->hub:Choice:Third");
    }

    [Test]
    public async Task Tunnels_and_threads_are_marked_and_functions_are_left_out()
    {
        var graph = Graph(("main.ink", """
            -> a
            === a ===
            -> b ->
            <- c
            ~ f()
            -> END
            === b ===
            ->->
            === c ===
            * [Thread choice] -> END
            === function f() ===
            ~ return 1
            """));

        await Assert.That(EdgeList(graph)).Contains("a->b:Tunnel");
        await Assert.That(EdgeList(graph)).Contains("a->c:Thread");
        await Assert.That(graph.Nodes.Select(n => n.Id)).DoesNotContain("f");
    }

    [Test]
    public async Task Graph_is_built_across_included_files()
    {
        var graph = Graph(
            ("main.ink", "INCLUDE other.ink\n-> other_knot"),
            ("other.ink", "=== other_knot ===\nHello\n-> END"));

        await Assert.That(graph.Find("other_knot")!.Range!.Value.File).IsEqualTo("other.ink");
        await Assert.That(graph.HasEdge(StoryGraph.StartId, "other_knot")).IsTrue();
    }

    [Test]
    public async Task Graph_survives_compile_errors()
    {
        var result = new InkCompilerService().Compile(InMemoryProject.OneFile("-> a\n=== a ===\n-> missing\n=== b ===\n-> a"));

        await Assert.That(result).IsTypeOf<CompileFailed>();
        await Assert.That(result.Graph.Nodes.Select(n => n.Id)).Contains("b");
        await Assert.That(result.Graph.HasEdge("b", "a")).IsTrue();
    }

    [Test]
    [Arguments("forest.clearing.2", "forest.clearing")]
    [Arguments("town.square.0.c-1", "town.square")]
    [Arguments("forest.0.c-0.7", "forest")]
    [Arguments("0.5", "(start)")]
    [Arguments("nowhere.0", null)]
    public async Task Runtime_paths_map_to_knots_and_stitches(string path, string? expected)
    {
        var graph = Graph(("main.ink", "-> forest\n=== forest ===\nA\n-> forest.clearing\n= clearing\nB\n-> town\n=== town ===\n-> square\n= square\nC\n-> END"));

        await Assert.That(graph.NodeForRuntimePath(path)).IsEqualTo(expected);
    }
}
