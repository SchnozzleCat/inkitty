using System.Text;
using Ink.Parsed;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.StoryMap;

/// <summary>Extracts a <see cref="StoryGraph"/> from the compiler's parsed story.</summary>
public static class StoryGraphBuilder
{
    private const int MaxLabelLength = 28;

    private const int MaxConditionLength = 32;

    /// <param name="mainFile">Project-relative main file, where the start node points.</param>
    /// <param name="resolveFile">Maps the file name in ink debug metadata to a project-relative path.</param>
    /// <param name="sourceText">Text of a project file, used to show conditions exactly as written.</param>
    public static StoryGraph Build(Story story, string mainFile, Func<string?, string?> resolveFile, Func<string, string?>? sourceText = null)
    {
        var conditions = new ConditionFormatter(resolveFile, sourceText);
        SourceRange? RangeOf(Ink.Parsed.Object? obj) => RangeOfMetadata(obj?.debugMetadata);
        SourceRange? IdRange(Identifier? id) => RangeOfMetadata(id?.debugMetadata);
        SourceRange? RangeOfMetadata(Ink.Runtime.DebugMetadata? metadata)
        {
            return metadata is { startLineNumber: > 0 } && resolveFile(metadata.fileName) is { } file ? metadata.ToRange(file) : null;
        }

        List<StoryNode> nodes = [new(StoryGraph.StartId, "Beginning", StoryNodeKind.Start, SourceRange.WholeLine(mainFile, 1))];
        foreach (var flow in story.FindAll<FlowBase>(f => f is Knot or Stitch && !f.isFunction))
        {
            if (IdOf(flow) is not { } id)
            {
                continue;
            }

            nodes.Add(flow is Stitch
                ? new StoryNode(id, flow.identifier.name, StoryNodeKind.Stitch, IdRange(flow.identifier), IdOf(flow.parent as FlowBase))
                : new StoryNode(id, flow.identifier.name, StoryNodeKind.Knot, IdRange(flow.identifier)));
        }

        List<StoryEdge> edges = [];
        foreach (var divert in story.FindAll<Divert>(d => !d.isFunctionCall && !d.isEmpty && d.target is not null))
        {
            // The compiler appends an implicit "-> DONE" to the top level; it has no source of its own.
            if (!divert.hasOwnDebugMetadata)
            {
                continue;
            }

            var owner = divert.ClosestFlowBase();
            if (owner is null || owner.isFunction)
            {
                continue;
            }

            var from = owner is Story ? StoryGraph.StartId : IdOf(owner);
            string? to;
            if (divert.isEnd || divert.isDone)
            {
                to = StoryGraph.EndId;
            }
            else if (divert.targetContent is FlowBase { isFunction: false } targetFlow)
            {
                to = targetFlow is Story ? StoryGraph.StartId : IdOf(targetFlow);
            }
            else if (divert.targetContent is Choice or Gather)
            {
                // A label: draw it as going to the label's knot/stitch, unless it's a loop within the same one.
                var labelOwner = divert.targetContent.ClosestFlowBase();
                to = labelOwner is Story ? StoryGraph.StartId : IdOf(labelOwner);
                if (to == from)
                {
                    continue;
                }
            }
            else
            {
                continue; // variable divert target, or unresolved (compile error)
            }

            if (from is null || to is null)
            {
                continue;
            }

            var choice = EnclosingChoice(divert);
            var kind = divert.isTunnel ? StoryEdgeKind.Tunnel
                : divert.isThread ? StoryEdgeKind.Thread
                : choice is not null ? StoryEdgeKind.Choice
                : StoryEdgeKind.Divert;
            edges.Add(new StoryEdge(from, to, kind, choice is null ? null : LabelOf(choice), RangeOf(choice ?? (Ink.Parsed.Object)divert), conditions.For(divert, choice)));
        }

        // A knot that starts straight with a stitch flows into it (ink's auto-enter rule).
        foreach (var knot in story.FindAll<Knot>(k => !k.isFunction))
        {
            if (knot.content.FirstOrDefault() is Stitch { hasParameters: false } first && IdOf(knot) is { } from && IdOf(first) is { } to)
            {
                edges.Add(new StoryEdge(from, to, StoryEdgeKind.Auto));
            }
        }

        if (edges.Any(e => e.To == StoryGraph.EndId))
        {
            nodes.Add(new StoryNode(StoryGraph.EndId, "END", StoryNodeKind.End, null));
        }

        return new StoryGraph(nodes, edges);
    }

    private static string? IdOf(FlowBase? flow) => flow switch
    {
        null or Story => null,
        Stitch { parent: Knot knot } => knot.identifier?.name is { } k && flow.identifier?.name is { } s ? $"{k}.{s}" : null,
        _ => flow.identifier?.name,
    };

    /// <summary>
    /// The choice a divert belongs to. Content on lines after a choice is a sibling of the choice in the
    /// weave (not a child), so look back through each enclosing weave for the nearest choice or gather.
    /// </summary>
    private static Choice? EnclosingChoice(Ink.Parsed.Object obj)
    {
        var node = obj;
        while (node.parent is { } parent and not FlowBase)
        {
            if (parent is Choice choice)
            {
                return choice;
            }

            if (parent is Weave weave)
            {
                for (var i = weave.content.IndexOf(node) - 1; i >= 0; i--)
                {
                    switch (weave.content[i])
                    {
                        case Choice previous:
                            return previous;
                        case Gather:
                            return null;
                    }
                }
            }

            node = parent;
        }

        return null;
    }

    private static string? LabelOf(Choice choice)
    {
        if (choice.isInvisibleDefault)
        {
            return "(fallback)";
        }

        var text = new StringBuilder();
        foreach (var part in (IEnumerable<ContentList?>)[choice.startContent, choice.choiceOnlyContent])
        {
            foreach (var piece in part?.FindAll<Text>() ?? [])
            {
                text.Append(piece.text);
            }
        }

        var label = string.Join(' ', text.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return label.Length switch
        {
            0 => null,
            > MaxLabelLength => label[..(MaxLabelLength - 1)].TrimEnd() + "…",
            _ => label,
        };
    }

    /// <summary>
    /// Works out which conditions guard a divert: the choice's own <c>{conditions}</c>, and any conditional
    /// blocks (<c>{ cond: -> a | -> b }</c>, <c>- cond:</c> / <c>- else:</c> branches) it sits inside.
    /// </summary>
    private sealed class ConditionFormatter(Func<string?, string?> resolveFile, Func<string, string?>? sourceText)
    {
        public string? For(Divert divert, Choice? choice)
        {
            List<string> parts = [];
            for (Ink.Parsed.Object? node = divert; node is not null and not FlowBase; node = node.parent)
            {
                if (node is ConditionalSingleBranch branch && node.parent is Conditional conditional)
                {
                    if (DescribeBranch(conditional, branch) is { } text)
                    {
                        parts.Insert(0, text);
                    }
                }
            }

            if (choice?.condition is { } choiceCondition)
            {
                var subs = choiceCondition is MultipleConditionExpression ? choiceCondition.content.OfType<Expression>() : [choiceCondition];
                parts.InsertRange(0, subs.Select(Written));
            }

            if (parts.Count == 0)
            {
                return null;
            }

            var joined = string.Join(" ", parts.Select(p => "{" + p + "}"));
            return joined.Length > MaxConditionLength ? joined[..(MaxConditionLength - 1)].TrimEnd() + "…" : joined;
        }

        private string? DescribeBranch(Conditional conditional, ConditionalSingleBranch branch)
        {
            if (branch.isElse)
            {
                return "else";
            }

            // { cond: a | b }: the first branch is the condition, the second is "not".
            if (branch.isTrueBranch && conditional.initialCondition is { } initial)
            {
                return Written(initial);
            }

            if (branch.matchingEquality && conditional.initialCondition is { } subject && branch.ownExpression is { } value)
            {
                return $"{Written(subject)} == {Written(value)}";
            }

            return branch.ownExpression is { } own ? Written(own) : null;
        }

        /// <summary>The expression as the author wrote it (from the source), or ink's rendering if unavailable.</summary>
        private string Written(Expression expression)
        {
            var metadata = expression.debugMetadata;
            if (metadata is { startLineNumber: > 0 } && metadata.startLineNumber == metadata.endLineNumber
                && resolveFile(metadata.fileName) is { } file && sourceText?.Invoke(file) is { } text)
            {
                var lines = text.Split('\n');
                if (metadata.startLineNumber <= lines.Length)
                {
                    var line = lines[metadata.startLineNumber - 1];
                    var start = Math.Clamp(metadata.startCharacterNumber - 1, 0, line.Length);
                    var end = Math.Clamp(metadata.endCharacterNumber - 1, start, line.Length);
                    var written = line[start..end].Trim().TrimStart('{').TrimEnd(':', '}').Trim();
                    if (written.Length > 0)
                    {
                        return Squash(written);
                    }
                }
            }

            return Squash(expression.ToString() ?? string.Empty);
        }

        private static string Squash(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
