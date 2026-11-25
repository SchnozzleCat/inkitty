using Inkitty.Core.Compilation;
using Inkitty.Core.Symbols;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.Symbols;

public class SymbolIndexTests
{
    private static SymbolIndex Index(params (string Path, string Text)[] files) =>
        new InkCompilerService().Compile(InMemoryProject.Snapshot(files)).Symbols;

    /// <summary>Position <paramref name="offset"/> characters into an occurrence of <paramref name="needle"/>.</summary>
    private static SourcePosition At(string text, string needle, int offset = 0, int occurrence = 1)
    {
        var index = -1;
        for (var n = 0; n < occurrence; n++)
        {
            index = text.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }

        var line = text[..index].Count(c => c == '\n') + 1;
        var column = index - text.LastIndexOf('\n', Math.Max(0, index - 1)); // 1-based
        return new SourcePosition(line, column + offset);
    }

    [Test]
    public async Task Divert_to_knot_resolves()
    {
        const string text = "-> forest\n=== forest ===\nTrees\n-> END";
        var index = Index(("main.ink", text));

        var reference = index.FindReferenceAt("main.ink", At(text, "forest"));

        await Assert.That(reference?.Target?.Kind).IsEqualTo(SymbolKind.Knot);
        await Assert.That(reference!.Target!.Range.Line).IsEqualTo(2);
    }

    [Test]
    public async Task Qualified_stitch_resolves_each_component()
    {
        const string text = "-> forest.clearing\n=== forest ===\nTrees\n-> END\n= clearing\nGrass\n-> END";
        var index = Index(("main.ink", text));

        var knotPart = index.FindReferenceAt("main.ink", At(text, "forest"));
        var stitchPart = index.FindReferenceAt("main.ink", At(text, "clearing"));

        await Assert.That(knotPart?.Target?.QualifiedName).IsEqualTo("forest");
        await Assert.That(stitchPart?.Target?.QualifiedName).IsEqualTo("forest.clearing");
        await Assert.That(stitchPart!.Target!.Range.Line).IsEqualTo(5);
    }

    [Test]
    public async Task Local_stitch_divert_resolves_within_knot()
    {
        const string text = "-> forest\n=== forest ===\n-> clearing\n= clearing\nGrass\n-> END";
        var index = Index(("main.ink", text));

        var reference = index.FindReferenceAt("main.ink", At(text, "clearing"));

        await Assert.That(reference?.Target?.QualifiedName).IsEqualTo("forest.clearing");
    }

    [Test]
    public async Task Cross_file_divert_points_at_other_file()
    {
        const string main = "INCLUDE forest.ink\n-> forest";
        var index = Index(("main.ink", main), ("forest.ink", "=== forest ===\nTrees\n-> END"));

        var reference = index.FindReferenceAt("main.ink", At(main, "forest", occurrence: 2));

        await Assert.That(reference?.Target?.Range.File).IsEqualTo("forest.ink");
        await Assert.That(reference!.Target!.Range.Line).IsEqualTo(1);
    }

    [Test]
    public async Task Label_divert_resolves()
    {
        const string text = "* (opt) [A] A\n- (done) Done\n-> opt\n";
        var index = Index(("main.ink", text));

        var reference = index.FindReferenceAt("main.ink", At(text, "-> opt", offset: 3));

        await Assert.That(reference?.Target?.Kind).IsEqualTo(SymbolKind.Label);
        await Assert.That(reference!.Target!.Range.Line).IsEqualTo(1);
    }

    [Test]
    public async Task Variables_constants_and_functions_resolve()
    {
        const string text = "VAR torch = false\nCONST MAX = 3\n~ torch = true\n{torch} {MAX} {double(2)}\n-> END\n=== function double(x) ===\n~ return x * 2";
        var index = Index(("main.ink", text));

        var torch = index.FindReferenceAt("main.ink", At(text, "{torch}", offset: 1));
        var max = index.FindReferenceAt("main.ink", At(text, "{MAX}", offset: 1));
        var func = index.FindReferenceAt("main.ink", At(text, "double(2)"));
        var param = index.FindReferenceAt("main.ink", At(text, "x * 2", offset: 0));

        await Assert.That(torch?.Target?.Kind).IsEqualTo(SymbolKind.Variable);
        await Assert.That(max?.Target?.Kind).IsEqualTo(SymbolKind.Constant);
        await Assert.That(func?.Target?.Kind).IsEqualTo(SymbolKind.Function);
        await Assert.That(param?.Target?.Kind).IsEqualTo(SymbolKind.Parameter);
    }

    [Test]
    public async Task Lists_and_items_resolve()
    {
        const string text = "LIST mood = happy, (sad)\n~ mood = happy\n{mood == mood.sad: x}\n-> END";
        var index = Index(("main.ink", text));

        var item = index.FindReferenceAt("main.ink", At(text, "~ mood = happy", offset: 9));
        var qualified = index.FindReferenceAt("main.ink", At(text, "mood.sad", offset: 5));

        await Assert.That(item?.Target?.Kind).IsEqualTo(SymbolKind.ListItem);
        await Assert.That(qualified?.Target?.QualifiedName).IsEqualTo("mood.sad");
    }

    [Test]
    public async Task Outline_lists_knots_stitches_and_functions_in_order()
    {
        var index = Index(("main.ink", "-> a\n=== a ===\n-> END\n= s\n-> END\n=== function f ===\n~ return 1"));

        await Assert.That(index.OutlineOf("main.ink").Select(d => d.QualifiedName)).IsEquivalentTo(
            ["a", "a.s", "f"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
