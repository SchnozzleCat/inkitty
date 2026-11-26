using Inkitty.Core.Outline;

namespace Inkitty.Core.Tests.Outline;

public class OutlineScannerTests
{
    /// <summary>The outline as indented "Kind Name [Detail] @line" lines.</summary>
    private static string Shape(string text) =>
        string.Concat(OutlineScanner.Flatten(OutlineScanner.Scan("main.ink", text)).Select(i =>
            $"{new string(' ', Depth(OutlineScanner.Scan("main.ink", text), i) * 2)}{i.Kind} {i.Name}{(i.Detail is null ? string.Empty : " " + i.Detail)} @{i.Line}\n"));

    private static int Depth(IReadOnlyList<OutlineItem> roots, OutlineItem target, int depth = 0)
    {
        foreach (var item in roots)
        {
            if (item.Key == target.Key)
            {
                return depth;
            }

            var inner = Depth(item.Children, target, depth + 1);
            if (inner >= 0)
            {
                return inner;
            }
        }

        return -1;
    }

    [Test]
    public async Task Outline_nests_stitches_choices_and_labelled_gathers()
    {
        const string text = """
            INCLUDE chapters/forest.ink
            VAR has_torch = false // lit?
            CONST MAX = 3
            LIST mood = calm, (nervous)
            EXTERNAL playSound(name)
            -> start

            === start ===
            Once upon a time.
            * (left) {has_torch} [Go left] -> forest
                * * [Deeper] Deep.
                - - (regroup) Back.
            + [Wait] -> start
            *   -> fallback_only
            - (after) Done.

            = side_stitch(x, -> y)
            * Side choice # tagged
            === function courage() ===
            ~ return 1
            """;

        await Assert.That(Shape(text)).IsEqualTo("""
            Include chapters/forest.ink @1
            Variable has_torch false @2
            Constant MAX 3 @3
            List mood calm, (nervous) @4
            External playSound (name) @5
            Knot start @8
              Choice Go left (left) {has_torch} @10
                Choice Deeper @11
                Gather regroup @12
              StickyChoice Wait @13
              Choice (fallback) @14
              Gather after @15
              Stitch side_stitch (x, -> y) @17
                Choice Side choice @18
            Function courage () @19

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public async Task Comments_and_conditional_branches_are_not_outlined()
    {
        const string text = """
            === k ===
            // * [Not a choice]
            /* === not_a_knot ===
            * [Nor this] */
            { x:
                - 1: one
                - else: other
            }
            * [Real]
            """;

        await Assert.That(Shape(text)).IsEqualTo("Knot k @1\n  Choice Real @9\n");
    }

    [Test]
    public async Task Ranges_point_at_names_and_item_at_finds_the_enclosing_element()
    {
        var items = OutlineScanner.Scan("a.ink", "=== start ===\nText\n* [Go] Gone.\n  More.\n= stitch\nX");
        var knot = items.Single();

        await Assert.That(knot.Range.File).IsEqualTo("a.ink");
        await Assert.That((knot.Range.Start.Column, knot.Range.End.Column)).IsEqualTo((5, 10));
        await Assert.That(OutlineScanner.ItemAt(items, 2)?.Name).IsEqualTo("start");
        await Assert.That(OutlineScanner.ItemAt(items, 4)?.Name).IsEqualTo("Go");
        await Assert.That(OutlineScanner.ItemAt(items, 6)?.Name).IsEqualTo("stitch");
    }

    [Test]
    public async Task Duplicate_names_get_distinct_keys()
    {
        var items = OutlineScanner.Flatten(OutlineScanner.Scan("a.ink", "=== k ===\n* [Yes]\n* [Yes]")).ToList();

        await Assert.That(items.Select(i => i.Key).Distinct().Count()).IsEqualTo(items.Count);
    }
}
