using Ink.Parsed;

namespace Inkitty.Core.Symbols;

/// <summary>
/// Builds a <see cref="SymbolIndex"/> from the compiler's parsed hierarchy. Works even when
/// compilation failed: the parsed story is kept, and paths are resolved here with ink's own
/// scoping rules (<c>Parsed.Path.ResolveFromContext</c>).
/// </summary>
public static class ParsedStorySymbolCollector
{
    /// <param name="story">The parsed story (<c>Compiler.parsedStory</c>).</param>
    /// <param name="resolveFile">Maps the file name in ink debug metadata to a project-relative path.</param>
    /// <param name="weavePointsResolved">
    /// True if the compiler got as far as code generation (so named choices/gathers are indexed).
    /// If false, we index them here so label diverts still resolve.
    /// </param>
    public static SymbolIndex Collect(Story story, Func<string?, string?> resolveFile, bool weavePointsResolved)
    {
        if (!weavePointsResolved)
        {
            TryResolveWeavePointNaming(story);
        }

        var collector = new Collector(resolveFile);
        collector.CollectDefinitions(story);
        collector.CollectReferences(story);
        return new SymbolIndex(collector.Definitions, collector.References);
    }

    private static void TryResolveWeavePointNaming(Story story)
    {
        try
        {
            story.ResolveWeavePointNaming();
        }
        catch (Exception)
        {
            // Duplicate names raise errors, which throw when no handler is attached.
            // Names resolved before the failure are still usable.
        }
    }

    private sealed class Collector(Func<string?, string?> resolveFile)
    {
        private readonly Dictionary<Ink.Parsed.Object, SymbolDefinition> _byObject = [with(ReferenceEqualityComparer.Instance)];
        private readonly Dictionary<string, SymbolDefinition> _globals = [];
        private readonly Dictionary<(FlowBase Flow, string Name), SymbolDefinition> _locals = [];

        public List<SymbolDefinition> Definitions { get; } = [];

        public List<SymbolReference> References { get; } = [];

        public void CollectDefinitions(Story story)
        {
            foreach (var flow in story.FindAll<FlowBase>(f => f is not Story))
            {
                var kind = flow switch
                {
                    { isFunction: true } => SymbolKind.Function,
                    Stitch => SymbolKind.Stitch,
                    _ => SymbolKind.Knot,
                };
                var container = flow is Stitch ? QualifiedNameOf(flow.parent as FlowBase) : null;
                Add(flow, flow.identifier, kind, container);

                foreach (var argument in flow.arguments ?? [])
                {
                    if (Define(argument.identifier, SymbolKind.Parameter, QualifiedNameOf(flow), qualified: false) is { } def)
                    {
                        _locals[(flow, def.Name)] = def;
                    }
                }
            }

            foreach (var choice in story.FindAll<Choice>(c => c.identifier is not null))
            {
                Add(choice, choice.identifier, SymbolKind.Label, QualifiedNameOf(choice.ClosestFlowBase()));
            }

            foreach (var gather in story.FindAll<Gather>(g => g.identifier is not null))
            {
                Add(gather, gather.identifier, SymbolKind.Label, QualifiedNameOf(gather.ClosestFlowBase()));
            }

            foreach (var assignment in story.FindAll<VariableAssignment>(a => a.isDeclaration))
            {
                if (assignment.listDefinition is not null)
                {
                    continue; // handled with lists below
                }

                if (assignment.isGlobalDeclaration)
                {
                    if (Define(assignment.variableIdentifier, SymbolKind.Variable, null, qualified: false) is { } def)
                    {
                        _globals.TryAdd(def.Name, def);
                    }
                }
                else if (assignment.ClosestFlowBase() is { } owner
                    && Define(assignment.variableIdentifier, SymbolKind.Temporary, QualifiedNameOf(owner), qualified: false) is { } temp)
                {
                    _locals.TryAdd((owner, temp.Name), temp);
                }
            }

            foreach (var constant in story.FindAll<ConstantDeclaration>())
            {
                if (Define(constant.constantIdentifier, SymbolKind.Constant, null, qualified: false) is { } def)
                {
                    _globals.TryAdd(def.Name, def);
                }
            }

            foreach (var list in story.FindAll<ListDefinition>())
            {
                if (Define(list.identifier, SymbolKind.List, null, qualified: false) is not { } listDef)
                {
                    continue;
                }

                _globals.TryAdd(listDef.Name, listDef);
                foreach (var item in list.itemDefinitions ?? [])
                {
                    if (Define(item.identifier, SymbolKind.ListItem, listDef.Name, qualified: true) is { } itemDef)
                    {
                        _globals.TryAdd(itemDef.QualifiedName, itemDef);
                        _globals.TryAdd(itemDef.Name, itemDef);
                    }
                }
            }

            foreach (var external in story.FindAll<ExternalDeclaration>())
            {
                if (Add(external, external.identifier, SymbolKind.External, null) is { } def)
                {
                    _globals.TryAdd(def.Name, def);
                }
            }
        }

        public void CollectReferences(Story story)
        {
            foreach (var divert in story.FindAll<Divert>())
            {
                CollectDivert(divert);
            }

            foreach (var variable in story.FindAll<VariableReference>())
            {
                CollectVariable(variable);
            }
        }

        private void CollectDivert(Divert divert)
        {
            var components = divert.target?.components;
            if (components is null || components.Count == 0 || divert.isEnd || divert.isDone)
            {
                return;
            }

            // Each path component links to the prefix it names: in -> a.b.c, clicking 'b' goes to a.b.
            for (var i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (RangeOf(component) is not { } range)
                {
                    continue;
                }

                SymbolDefinition? target = null;
                var resolved = i == components.Count - 1 && divert.targetContent is not null
                    ? divert.targetContent
                    : TryResolve(new Ink.Parsed.Path(components.GetRange(0, i + 1)), divert);

                if (resolved is not null)
                {
                    target = _byObject.GetValueOrDefault(resolved);
                }
                else if (components.Count == 1)
                {
                    // Variable divert target (-> my_var), parameter, or external function.
                    target = ResolveVariable(component.name, divert);
                }

                References.Add(new SymbolReference(component.name, range, target));
            }
        }

        private void CollectVariable(VariableReference variable)
        {
            var ids = variable.pathIdentifiers;
            if (ids is null || ids.Count == 0)
            {
                return;
            }

            if (ids.Count == 1)
            {
                if (RangeOf(ids[0]) is { } range)
                {
                    var target = ResolveVariable(ids[0].name, variable)
                        ?? (TryResolve(new Ink.Parsed.Path(ids), variable) is { } obj ? _byObject.GetValueOrDefault(obj) : null);
                    References.Add(new SymbolReference(ids[0].name, range, target));
                }

                return;
            }

            // Either a read count (knot.stitch) or a list item (list.item).
            var listItem = _globals.GetValueOrDefault(string.Join('.', ids.Select(i => i.name)));
            for (var i = 0; i < ids.Count; i++)
            {
                if (RangeOf(ids[i]) is not { } range)
                {
                    continue;
                }

                SymbolDefinition? target;
                if (listItem is not null)
                {
                    target = i == 0 ? _globals.GetValueOrDefault(ids[0].name) : listItem;
                }
                else
                {
                    var resolved = TryResolve(new Ink.Parsed.Path(ids.GetRange(0, i + 1)), variable);
                    target = resolved is null ? null : _byObject.GetValueOrDefault(resolved);
                }

                References.Add(new SymbolReference(ids[i].name, range, target));
            }
        }

        private SymbolDefinition? ResolveVariable(string? name, Ink.Parsed.Object context)
        {
            if (name is null)
            {
                return null;
            }

            // Same rules as FlowBase.ResolveVariableWithName: arguments and temporaries of the
            // owning knot/stitch, then globals.
            if (context.ClosestFlowBase() is { } owner and not Story && _locals.TryGetValue((owner, name), out var local))
            {
                return local;
            }

            return _globals.GetValueOrDefault(name);
        }

        private static Ink.Parsed.Object? TryResolve(Ink.Parsed.Path path, Ink.Parsed.Object context)
        {
            try
            {
                return path.ResolveFromContext(context);
            }
            catch (Exception)
            {
                return null; // half-parsed hierarchies can throw on missing pieces
            }
        }

        private SymbolDefinition? Add(Ink.Parsed.Object obj, Identifier? identifier, SymbolKind kind, string? container)
        {
            var def = Define(identifier, kind, container, qualified: true);
            if (def is not null)
            {
                _byObject[obj] = def;
            }

            return def;
        }

        private SymbolDefinition? Define(Identifier? identifier, SymbolKind kind, string? container, bool qualified)
        {
            if (identifier?.name is not { Length: > 0 } name || RangeOf(identifier) is not { } range)
            {
                return null;
            }

            var qualifiedName = qualified && container is not null ? $"{container}.{name}" : name;
            var def = new SymbolDefinition(name, qualifiedName, kind, range, container);
            Definitions.Add(def);
            return def;
        }

        private SourceRange? RangeOf(Identifier? identifier)
        {
            var metadata = identifier?.debugMetadata;
            if (metadata is null || metadata.startLineNumber < 1)
            {
                return null;
            }

            return resolveFile(metadata.fileName) is { } file ? metadata.ToRange(file) : null;
        }

        private static string? QualifiedNameOf(FlowBase? flow) => flow switch
        {
            null or Story => null,
            Stitch { parent: FlowBase knot and not Story } => $"{knot.identifier?.name}.{flow.identifier?.name}",
            _ => flow.identifier?.name,
        };
    }
}
