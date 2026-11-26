using System.Globalization;

namespace Inkitty.Core.Playback;

public enum StoryVariableKind
{
    Boolean,
    WholeNumber,
    FractionalNumber,
    Text,
    List,
    DivertTarget,
    VariablePointer,
    Other,
}

/// <summary>A global ink variable's value at one point in a playthrough.</summary>
/// <param name="Name">The variable name (as declared with VAR, or a CONST/LIST name).</param>
/// <param name="Value">The value as ink would print it (lists as "a, b"; strings quoted; divert targets as -> path).</param>
/// <param name="DefaultValue">The value when the story started, formatted the same way.</param>
public sealed record StoryVariable(string Name, StoryVariableKind Kind, string Value, string DefaultValue)
{
    /// <summary>True if the story has changed this variable from its declared value.</summary>
    public bool IsChanged => !string.Equals(Value, DefaultValue, StringComparison.Ordinal);

    internal static StoryVariable From(string name, Ink.Runtime.Object? value, Ink.Runtime.Object? defaultValue)
    {
        var (kind, text) = Describe(value);
        return new StoryVariable(name, kind, text, Describe(defaultValue).Text);
    }

    private static (StoryVariableKind Kind, string Text) Describe(Ink.Runtime.Object? value) => value switch
    {
        Ink.Runtime.BoolValue b => (StoryVariableKind.Boolean, b.value ? "true" : "false"),
        Ink.Runtime.IntValue i => (StoryVariableKind.WholeNumber, i.value.ToString(CultureInfo.InvariantCulture)),
        Ink.Runtime.FloatValue f => (StoryVariableKind.FractionalNumber, f.value.ToString("0.0###", CultureInfo.InvariantCulture)),
        Ink.Runtime.StringValue s => (StoryVariableKind.Text, $"\"{s.value}\""),
        Ink.Runtime.ListValue l => (StoryVariableKind.List, l.value.Count == 0 ? "(empty)" : l.value.ToString()),
        Ink.Runtime.DivertTargetValue d => (StoryVariableKind.DivertTarget, $"-> {d.targetPath.componentsString}"),
        Ink.Runtime.VariablePointerValue p => (StoryVariableKind.VariablePointer, $"ref {p.variableName}"),
        null => (StoryVariableKind.Other, string.Empty),
        _ => (StoryVariableKind.Other, value.ToString() ?? string.Empty),
    };
}
