using Inkitty.Core.Symbols;

namespace Inkitty.Core.Tests.Symbols;

public class FallbackSymbolScannerTests
{
    private const string Main = """
        VAR torch = false
        -> forest.clearing
        === forest ===
        * (light) [Light] -> clearing
        = clearing
        Grass. {light: Bright.}
        -> END
        === function double(x) ===
        ~ return x * 2
        """;

    private static SymbolReference? At(string text, int line, int column, params (string File, string Text)[] others)
    {
        var defs = FallbackSymbolScanner.ScanDefinitions([("main.ink", text), .. others]);
        return FallbackSymbolScanner.ResolveAt("main.ink", text, new SourcePosition(line, column), defs);
    }

    [Test]
    public async Task Finds_definitions()
    {
        var defs = FallbackSymbolScanner.ScanDefinitions([("main.ink", Main)]);

        await Assert.That(defs.Select(d => $"{d.Kind}:{d.QualifiedName}")).IsEquivalentTo(
            ["Variable:torch", "Knot:forest", "Label:forest.light", "Stitch:forest.clearing", "Function:double"]);
    }

    [Test]
    public async Task Resolves_qualified_divert_components()
    {
        var knot = At(Main, 2, 5);
        var stitch = At(Main, 2, 12);

        await Assert.That(knot?.Target?.QualifiedName).IsEqualTo("forest");
        await Assert.That(stitch?.Target?.QualifiedName).IsEqualTo("forest.clearing");
        await Assert.That(stitch!.Range.Start.Column).IsEqualTo(11);
    }

    [Test]
    public async Task Resolves_local_stitch_and_label_by_scope()
    {
        var stitch = At(Main, 4, 25);
        var label = At(Main, 6, 10);

        await Assert.That(stitch?.Target?.QualifiedName).IsEqualTo("forest.clearing");
        await Assert.That(label?.Target?.Kind).IsEqualTo(SymbolKind.Label);
    }

    [Test]
    public async Task Resolves_across_files()
    {
        const string text = "-> town";
        var reference = At(text, 1, 5, ("town.ink", "=== town ===\nHi\n-> END"));

        await Assert.That(reference?.Target?.Range.File).IsEqualTo("town.ink");
    }

    [Test]
    public async Task Returns_null_off_identifiers()
    {
        await Assert.That(At(Main, 2, 2)).IsNull();
    }
}
