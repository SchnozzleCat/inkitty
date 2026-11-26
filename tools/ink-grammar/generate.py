"""Generates src/Inkitty.App/Assets/Grammars/ink.tmLanguage.json, Inkitty's TextMate grammar for ink.

Scopes follow common TextMate conventions so every bundled VS Code theme colours them:
knot/stitch names, labels and divert targets are functions, -> and choice/gather bullets are control
keywords, END/DONE are language constants, and logic is tokenised as an
expression (variables, operators, numbers, strings, built-in and user functions).

Usage: python3 tools/ink-grammar/generate.py src/Inkitty.App/Assets/Grammars/ink.tmLanguage.json
"""
import json, sys

def cap(*names):
    return {str(i + 1): {"name": n} for i, n in enumerate(names) if n}

IDENT = r"[^\W\d]\w*"
PATH = IDENT + r"(?:\." + IDENT + r")*"

repo = {}

repo["comments"] = {"patterns": [
    {"name": "comment.block.ink", "begin": r"/\*", "end": r"\*/",
     "beginCaptures": {"0": {"name": "punctuation.definition.comment.begin.ink"}},
     "endCaptures": {"0": {"name": "punctuation.definition.comment.end.ink"}}},
    {"name": "comment.line.double-slash.ink", "match": r"(//).*$",
     "captures": cap("punctuation.definition.comment.ink")},
]}

repo["todo"] = {"name": "comment.line.todo.ink", "match": r"^\s*(TODO)\b\s*(:)?.*$",
                "captures": cap("keyword.other.todo.ink", "punctuation.separator.todo.ink")}

repo["include"] = {"match": r"^\s*(INCLUDE)\b\s*(.*?)\s*(?://.*)?$",
                   "captures": cap("keyword.control.import.ink", "string.unquoted.path.ink")}

repo["parameters"] = {"begin": r"\(", "end": r"\)",
    "beginCaptures": {"0": {"name": "punctuation.definition.parameters.begin.ink"}},
    "endCaptures": {"0": {"name": "punctuation.definition.parameters.end.ink"}},
    "patterns": [
        {"match": r"\bref\b", "name": "storage.modifier.ref.ink"},
        {"match": r"->", "name": "keyword.control.flow.divert.ink"},
        {"match": r",", "name": "punctuation.separator.parameter.ink"},
        {"match": IDENT, "name": "variable.parameter.ink"},
    ]}

repo["external"] = {"begin": r"^\s*(EXTERNAL)\b\s*(" + IDENT + r")?", "end": r"$",
    "beginCaptures": cap("storage.type.external.ink", "entity.name.function.ink"),
    "patterns": [{"include": "#parameters"}, {"include": "#comments"}]}

repo["knot"] = {"name": "meta.knot.declaration.ink",
    "begin": r"^\s*(={2,})\s*(?:(function)\b\s*)?(" + IDENT + r")?", "end": r"$",
    "beginCaptures": cap("punctuation.definition.knot.ink storage.type.knot.ink", "storage.type.function.ink", "entity.name.function.knot.ink"),
    "patterns": [{"include": "#parameters"}, {"match": r"=+", "name": "punctuation.definition.knot.ink storage.type.knot.ink"}, {"include": "#comments"}]}

repo["stitch"] = {"name": "meta.stitch.declaration.ink",
    "begin": r"^\s*(=)(?!=)\s*(" + IDENT + r")?", "end": r"$",
    "beginCaptures": cap("punctuation.definition.stitch.ink storage.type.stitch.ink", "entity.name.function.stitch.ink"),
    "patterns": [{"include": "#parameters"}, {"include": "#comments"}]}

repo["var"] = {"name": "meta.declaration.variable.ink",
    "begin": r"^\s*(?:(VAR)|(CONST))\b\s*(" + IDENT + r")?\s*(=)?", "end": r"$",
    "beginCaptures": cap("storage.type.var.ink", "storage.type.const.ink", "variable.other.declaration.ink", "keyword.operator.assignment.ink"),
    "patterns": [{"include": "#expression"}]}

repo["list"] = {"name": "meta.declaration.list.ink",
    "begin": r"^\s*(LIST)\b\s*(" + IDENT + r")?\s*(=)?", "end": r"$",
    "beginCaptures": cap("storage.type.list.ink", "entity.name.type.list.ink", "keyword.operator.assignment.ink"),
    "patterns": [
        {"include": "#comments"},
        {"match": r"[()]", "name": "punctuation.definition.list-item.selected.ink"},
        {"match": r",", "name": "punctuation.separator.list.ink"},
        {"match": r"=", "name": "keyword.operator.assignment.ink"},
        {"include": "#numbers"},
        {"match": IDENT, "name": "variable.other.enummember.ink"},
    ]}

repo["numbers"] = {"match": r"\b\d+(?:\.\d+)?\b", "name": "constant.numeric.ink"}

repo["string"] = {"name": "string.quoted.double.ink", "begin": r'"', "end": r'"|$',
    "beginCaptures": {"0": {"name": "punctuation.definition.string.begin.ink"}},
    "endCaptures": {"0": {"name": "punctuation.definition.string.end.ink"}},
    "patterns": [{"include": "#escape"}, {"include": "#inline"}]}

BUILTINS = r"CHOICE_COUNT|TURNS|TURNS_SINCE|READ_COUNT|SEED_RANDOM|RANDOM|INT|FLOOR|FLOAT|POW|MIN|MAX|LIST_\w+"
repo["expression"] = {"patterns": [
    {"include": "#comments"},
    {"include": "#string"},
    {"include": "#numbers"},
    {"match": r"\b(?:true|false)\b", "name": "constant.language.boolean.ink"},
    {"match": r"\b(?:not|and|or|mod|has|hasnt)\b", "name": "keyword.operator.word.ink"},
    {"match": r"\b(?:return)\b", "name": "keyword.control.return.ink"},
    {"match": r"\b(temp)\b\s*(" + IDENT + r")?", "captures": cap("storage.type.temp.ink", "variable.other.declaration.ink")},
    {"match": r"(->)\s*(" + PATH + r")?", "captures": cap("keyword.control.flow.divert.ink", "entity.name.function.divert-target.ink")},
    {"match": r"\b(" + BUILTINS + r")\b(?=\s*\()", "name": "support.function.builtin.ink"},
    {"match": r"\b(" + IDENT + r")\b(?=\s*\()", "name": "entity.name.function.call.ink"},
    {"match": r"==|!=|<=|>=|&&|\|\||\+\+|--|[-+*/%<>=!?^]", "name": "keyword.operator.ink"},
    {"match": r"[(),]", "name": "punctuation.ink"},
    {"match": r"\b" + IDENT + r"(?:\." + IDENT + r")*\b", "name": "variable.other.ink"},
]}

repo["escape"] = {"match": r"\\.", "name": "constant.character.escape.ink"}

repo["glue"] = {"match": r"<>", "name": "keyword.operator.glue.ink"}

repo["tag"] = {"match": r"(#)\s*((?:(?!//)[^#\n])*)",
               "captures": cap("punctuation.definition.tag.ink", "entity.name.tag.ink")}

repo["divert"] = {"patterns": [
    {"match": r"->->", "name": "keyword.control.flow.tunnel-return.ink"},
    {"begin": r"(->|<-)\s*(" + PATH + r")\s*(\()", "end": r"\)",
     "beginCaptures": cap("keyword.control.flow.divert.ink", "entity.name.function.divert-target.ink", "punctuation.definition.arguments.begin.ink"),
     "endCaptures": {"0": {"name": "punctuation.definition.arguments.end.ink"}},
     "patterns": [{"include": "#expression"}]},
    {"match": r"(->|<-)\s*(?:\b(END|DONE)\b|(" + PATH + r"))?",
     "captures": cap("keyword.control.flow.divert.ink", "constant.language.flow.ink", "entity.name.function.divert-target.ink")},
]}

SEQ = r"stopping|cycle|shuffle(?:\s+(?:once|stopping))?|once"
repo["inline"] = {"patterns": [
    # {condition: a | b}
    {"name": "meta.conditional.inline.ink",
     "begin": r"(\{)([^{}:|\n]*)(:)", "end": r"\}",
     "beginCaptures": {"1": {"name": "punctuation.section.embedded.begin.ink"},
                       "2": {"patterns": [{"include": "#expression"}]},
                       "3": {"name": "punctuation.separator.condition.ink"}},
     "endCaptures": {"0": {"name": "punctuation.section.embedded.end.ink"}},
     "patterns": [{"match": r"\|", "name": "keyword.operator.branch.ink"}, {"include": "#content"}]},
    # {&a|b} {!a|b} {~a|b} sequences with a type symbol, and {a|b}
    {"name": "meta.sequence.inline.ink",
     "begin": r"(\{)\s*([&!~$])?(?=[^{}]*\|)", "end": r"\}",
     "beginCaptures": cap("punctuation.section.embedded.begin.ink", "keyword.control.sequence.ink"),
     "endCaptures": {"0": {"name": "punctuation.section.embedded.end.ink"}},
     "patterns": [{"match": r"\|", "name": "keyword.operator.branch.ink"}, {"include": "#content"}]},
    # {expression}
    {"name": "meta.expression.inline.ink",
     "begin": r"\{", "end": r"\}",
     "beginCaptures": {"0": {"name": "punctuation.section.embedded.begin.ink"}},
     "endCaptures": {"0": {"name": "punctuation.section.embedded.end.ink"}},
     "patterns": [{"include": "#expression"}]},
]}

# Text and everything that can appear in it.
repo["content"] = {"patterns": [
    {"include": "#comments"},
    {"include": "#escape"},
    {"include": "#glue"},
    {"include": "#tag"},
    {"include": "#divert"},
    {"include": "#inline"},
]}

LABEL = r"(?:\s*(\()\s*(" + IDENT + r")\s*(\)))?"
repo["choice"] = {"name": "meta.choice.ink",
    "begin": r"^\s*((?:[*+]\s*)+)" + LABEL, "end": r"$",
    "beginCaptures": cap("keyword.control.choice.ink", "punctuation.definition.label.ink", "entity.name.function.label.ink", "punctuation.definition.label.ink"),
    "patterns": [
        {"name": "meta.choice-only.ink", "begin": r"\[", "end": r"\]",
         "beginCaptures": {"0": {"name": "punctuation.section.choice-only.begin.ink"}},
         "endCaptures": {"0": {"name": "punctuation.section.choice-only.end.ink"}},
         "contentName": "markup.italic.choice-only.ink",
         "patterns": [{"include": "#content"}]},
        {"include": "#content"},
    ]}

repo["gather"] = {"name": "meta.gather.ink",
    "begin": r"^\s*((?:-(?!>)\s*)+)" + LABEL, "end": r"$",
    "beginCaptures": cap("keyword.control.gather.ink", "punctuation.definition.label.ink", "entity.name.function.label.ink", "punctuation.definition.label.ink"),
    "patterns": [{"include": "#content"}]}

repo["logic"] = {"name": "meta.logic.ink",
    "begin": r"^\s*(~)", "end": r"$",
    "beginCaptures": cap("keyword.control.logic.ink"),
    "patterns": [{"include": "#expression"}]}

repo["block"] = {"name": "meta.block.conditional.ink",
    "begin": r"^\s*(\{)(?=[^}]*$)", "end": r"\}",
    "beginCaptures": cap("punctuation.section.block.begin.ink"),
    "endCaptures": {"0": {"name": "punctuation.section.block.end.ink"}},
    "patterns": [
        # Header: { stopping:  /  { condition:
        {"match": r"\G\s*(" + SEQ + r")\s*(:)", "captures": cap("keyword.control.sequence.ink", "punctuation.separator.condition.ink")},
        {"match": r"\G([^:\n]*)(:)\s*(?=$|//)", "captures": {"1": {"patterns": [{"include": "#expression"}]}, "2": {"name": "punctuation.separator.condition.ink"}}},
        {"match": r"^\s*(-)\s*(else)\s*(:)", "captures": cap("keyword.control.gather.ink", "keyword.control.conditional.else.ink", "punctuation.separator.condition.ink")},
        {"match": r"^\s*(-)(?!>)\s*([^:{}\n]+?)\s*(:)", "captures": {"1": {"name": "keyword.control.gather.ink"}, "2": {"patterns": [{"include": "#expression"}]}, "3": {"name": "punctuation.separator.condition.ink"}}},
        {"include": "#line"},
    ]}

repo["line"] = {"patterns": [
    {"include": "#comments"},
    {"include": "#todo"},
    {"include": "#choice"},
    {"include": "#gather"},
    {"include": "#logic"},
    {"include": "#block"},
    {"include": "#content"},
]}

grammar = {
    "name": "ink",
    "scopeName": "source.ink",
    "fileTypes": ["ink", "ink2"],
    "uuid": "6f0e8b8b-8c1a-4d6e-9f43-3c2d5b1a7e10",
    "patterns": [
        {"include": "#comments"},
        {"include": "#include"},
        {"include": "#external"},
        {"include": "#var"},
        {"include": "#list"},
        {"include": "#knot"},
        {"include": "#stitch"},
        {"include": "#line"},
    ],
    "repository": repo,
}
json.dump(grammar, open(sys.argv[1], "w"), indent=2)
