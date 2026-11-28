using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;
using Inkitty.Core.Symbols;

namespace Inkitty.Core.Playback;

/// <summary>How hard <see cref="ReachabilityChecker"/> tries.</summary>
/// <param name="Runs">
/// Playthroughs to make. Each continues from a point a previous playthrough reached (or, sometimes,
/// from the beginning) and makes up to <paramref name="ChoicesPerRun"/> choices.
/// </param>
/// <param name="ChoicesPerRun">Choices each playthrough makes before the next one starts.</param>
/// <param name="MaxSavedPoints">Most story states kept to continue from (each is a few KB).</param>
/// <param name="RunTimeBudget">Wall-clock limit for one playthrough, so an endless loop doesn't hang the check.</param>
/// <param name="MaxLinesPerRun">Stops a playthrough that outputs this many lines without a choice.</param>
/// <param name="Seed">Seeds the choice picking and the story's own randomness, so a check is repeatable.</param>
public sealed record ReachabilityOptions(
    int Runs = ReachabilityOptions.DefaultRuns,
    int ChoicesPerRun = 20,
    int MaxSavedPoints = 20_000,
    TimeSpan? RunTimeBudget = null,
    int MaxLinesPerRun = 10_000,
    int Seed = 0)
{
    public const int DefaultRuns = 5000;

    public TimeSpan TimeBudget => RunTimeBudget ?? TimeSpan.FromSeconds(2);
}

/// <summary>The outcome of a reachability check.</summary>
/// <param name="Unreached">Lines with story text or choices that no playthrough reached, in file/line order.</param>
/// <param name="CoverableLines">Lines with story text or choices.</param>
/// <param name="Runs">Playthroughs made (fewer than asked for if every line was reached early).</param>
public sealed record ReachabilityReport(IReadOnlyList<SourceRange> Unreached, int CoverableLines, int Runs)
{
    public int ReachedLines => CoverableLines - Unreached.Count;

    /// <summary>One warning per line that wasn't reached.</summary>
    public IReadOnlyList<Issue> ToIssues() =>
    [
        .. Unreached.Select(range => new Issue(
            IssueSeverity.Warning,
            $"Not reached in {Runs} test {(Runs == 1 ? "playthrough" : "playthroughs")}",
            range,
            IssueOrigin.Reachability)),
    ];
}

/// <summary>
/// Plays a story many times with automatically picked choices and reports the lines that never ran.
/// A line "runs" when its text is output or a choice written on it is offered. Lines with only logic
/// (<c>~ x = 1</c>, diverts) aren't checked. Not reaching a line is evidence, not proof, that it
/// can't be reached: conditions may need choices the playthroughs didn't happen to make.
/// </summary>
/// <remarks>
/// Restarting every playthrough from the beginning rarely gets deep into a branching story: a line
/// behind ten particular choices needs all ten picked in a row. So, like a coverage-guided fuzzer
/// ("Go-Explore"), the checker saves the story's state at every new kind of choice point it finds (where
/// it is, plus the variables) and starts most playthroughs from one of those, preferring points that
/// still have choices nobody took and points it has started from least.
/// </remarks>
public static class ReachabilityChecker
{
    /// <summary>Chance of a playthrough starting from the beginning (with a new random seed).</summary>
    private const double RestartChance = 0.05;

    private const float SliceMilliseconds = 20;

    /// <param name="compiled">The compiled story. It isn't modified; the check plays its own copy.</param>
    /// <param name="snapshot">The snapshot it was compiled from (maps ink's file names to project files).</param>
    /// <param name="progress">Called with the number of playthroughs finished, from the checking thread.</param>
    public static async Task<ReachabilityReport> CheckAsync(
        Ink.Runtime.Story compiled,
        ProjectSnapshot snapshot,
        ReachabilityOptions options,
        Action<int>? progress = null,
        TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        var explorer = new Explorer(compiled, snapshot, options, time ?? TimeProvider.System, cancellationToken);
        var runs = 0;
        try
        {
            while (runs < Math.Max(1, options.Runs) && !explorer.ReachedEverything)
            {
                cancellationToken.ThrowIfCancellationRequested();
                explorer.PlayOnce(fromStart: runs == 0);
                runs++;
                progress?.Invoke(runs);

                // Let the UI breathe where there's only one thread (the browser).
                await Task.Yield();
            }
        }
        finally
        {
            explorer.Dispose();
        }

        return new ReachabilityReport(explorer.Unreached(), explorer.CoverableLines, runs);
    }

    /// <summary>A kind of choice point the exploration has found, and a saved state to continue from.</summary>
    private sealed class SavedPoint(string state, int choiceCount)
    {
        public string State { get; } = state;

        /// <summary>Choices on offer here that no playthrough has taken from this point yet.</summary>
        public List<int> Untried { get; } = [.. Enumerable.Range(0, choiceCount)];

        public int TimesStartedFrom { get; set; }

        public double Weight => (Untried.Count > 0 ? 4.0 : 1.0) / Math.Sqrt(TimesStartedFrom + 1);
    }

    private sealed class Explorer : IDisposable
    {
        private readonly Ink.Runtime.Story _compiled;
        private readonly ReachabilityOptions _options;
        private readonly TimeProvider _time;
        private readonly CancellationToken _cancellationToken;
        private readonly LineMap _lines;
        private readonly HashSet<LineKey> _coverable;
        private readonly HashSet<LineKey> _reached = [];
        private readonly Dictionary<string, SavedPoint> _pointsByKey = [];
        private readonly List<SavedPoint> _points = [];
        private readonly Dictionary<string, int> _timesTaken = [];
        private readonly Random _random;
        private readonly System.Text.StringBuilder _key = new();
        private Ink.Runtime.Story _story;
        private bool _errored;
        private int _restarts;

        public Explorer(Ink.Runtime.Story compiled, ProjectSnapshot snapshot, ReachabilityOptions options, TimeProvider time, CancellationToken cancellationToken)
        {
            _compiled = compiled;
            _options = options;
            _time = time;
            _cancellationToken = cancellationToken;
            _random = new Random(options.Seed);
            _lines = new LineMap(snapshot);
            _story = Fresh();
            _coverable = _lines.CollectCoverable(_story.mainContentContainer);
        }

        public int CoverableLines => _coverable.Count;

        public bool ReachedEverything => _reached.Count >= _coverable.Count && _coverable.IsSubsetOf(_reached);

        public IReadOnlyList<SourceRange> Unreached() =>
        [
            .. _coverable.Except(_reached)
                .OrderBy(l => l.File, StringComparer.Ordinal)
                .ThenBy(l => l.Line)
                .Select(l => SourceRange.WholeLine(l.File, l.Line)),
        ];

        public void Dispose() => _story.onError -= OnError;

        /// <summary>One playthrough: from a saved point (or the beginning), up to <see cref="ReachabilityOptions.ChoicesPerRun"/> choices.</summary>
        public void PlayOnce(bool fromStart)
        {
            _errored = false;
            var start = _time.GetTimestamp();
            bool clean;
            try
            {
                clean = Play(fromStart || _points.Count == 0 || _random.NextDouble() < RestartChance, start);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                clean = false; // ink's runtime throws plain .NET exceptions from asserts and story maths
            }

            // A run cut short (mid-ContinueAsync, or by an error) leaves the story unusable.
            if (!clean || !_story.asyncContinueComplete)
            {
                _story.onError -= OnError;
                _story = Fresh();
            }
        }

        private bool Play(bool fromStart, long start)
        {
            int? first = null;
            if (fromStart)
            {
                _story.ResetState();
                _story.state.storySeed = _options.Seed + _restarts++; // vary RANDOM() and shuffles
                if (!ContinueToChoice(start))
                {
                    return false;
                }
            }
            else
            {
                var point = PickPoint();
                point.TimesStartedFrom++;
                _story.state.LoadJson(point.State);
                if (point.Untried.Count > 0)
                {
                    var i = _random.Next(point.Untried.Count);
                    first = point.Untried[i];
                    point.Untried.RemoveAt(i);
                }
            }

            for (var choicesMade = 0; choicesMade < _options.ChoicesPerRun; choicesMade++)
            {
                var choices = _story.currentChoices;
                if (choices.Count == 0)
                {
                    return true; // the end
                }

                if (choicesMade > 0 || fromStart)
                {
                    Save(choices);
                }

                var index = first is { } f && f < choices.Count ? f : PickChoice(choices);
                first = null;
                var path = choices[index].sourcePath ?? string.Empty;
                _timesTaken[path] = _timesTaken.GetValueOrDefault(path) + 1;
                _story.ChooseChoiceIndex(index);
                if (!ContinueToChoice(start))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Continues until choices or the end, marking what was output. False if cut short.</summary>
        private bool ContinueToChoice(long start)
        {
            var outputLines = 0;
            while (_story.canContinue)
            {
                do
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    _story.ContinueAsync(SliceMilliseconds);
                    if (_time.GetElapsedTime(start) > _options.TimeBudget)
                    {
                        return false;
                    }
                }
                while (!_story.asyncContinueComplete);

                _lines.MarkOutput(_story.state.outputStream, _reached);
                if (_errored || ++outputLines > _options.MaxLinesPerRun)
                {
                    return false;
                }
            }

            foreach (var choice in _story.currentChoices)
            {
                _lines.MarkChoice(choice.sourcePath, _reached);
            }

            return true;
        }

        /// <summary>
        /// Saves the state if it's a new kind of choice point: different choices on offer, or different
        /// variable values (which conditions further on may depend on).
        /// </summary>
        private void Save(List<Ink.Runtime.Choice> choices)
        {
            if (_points.Count >= _options.MaxSavedPoints)
            {
                return;
            }

            _key.Clear();
            foreach (var choice in choices)
            {
                _key.Append(choice.sourcePath).Append(';');
            }

            _key.Append('|');
            var variables = _story.variablesState;
            foreach (var name in variables)
            {
                _key.Append(name).Append('=').Append(variables[name]).Append(';');
            }

            var key = _key.ToString();
            if (!_pointsByKey.ContainsKey(key))
            {
                var point = new SavedPoint(_story.state.ToJson(), choices.Count);
                _pointsByKey[key] = point;
                _points.Add(point);
            }
        }

        /// <summary>A saved point, weighted towards ones with untried choices and ones used least.</summary>
        private SavedPoint PickPoint()
        {
            var total = 0.0;
            foreach (var point in _points)
            {
                total += point.Weight;
            }

            var target = _random.NextDouble() * total;
            foreach (var point in _points)
            {
                target -= point.Weight;
                if (target <= 0)
                {
                    return point;
                }
            }

            return _points[^1];
        }

        /// <summary>Usually the choice taken least often so far (ties broken randomly), sometimes any choice.</summary>
        private int PickChoice(List<Ink.Runtime.Choice> choices)
        {
            if (_random.NextDouble() < 0.25)
            {
                return _random.Next(choices.Count);
            }

            var fewest = choices.Min(c => _timesTaken.GetValueOrDefault(c.sourcePath ?? string.Empty));
            var candidates = Enumerable.Range(0, choices.Count)
                .Where(i => _timesTaken.GetValueOrDefault(choices[i].sourcePath ?? string.Empty) == fewest)
                .ToList();
            return candidates[_random.Next(candidates.Count)];
        }

        private Ink.Runtime.Story Fresh()
        {
            var story = PlaySession.CopyOf(_compiled);
            story.allowExternalFunctionFallbacks = true; // EXTERNAL functions have no game to bind them here
            story.onError += OnError;
            return story;
        }

        private void OnError(string message, Ink.ErrorType type) => _errored |= type == Ink.ErrorType.Error;
    }

    private readonly record struct LineKey(string File, int Line);

    /// <summary>Maps runtime content to project lines.</summary>
    private sealed class LineMap(ProjectSnapshot snapshot)
    {
        /// <summary>Line of each (visible) choice point, by runtime path (what <c>Choice.sourcePath</c> holds).</summary>
        private readonly Dictionary<string, LineKey> _choiceLines = [];

        public HashSet<LineKey> CollectCoverable(Ink.Runtime.Container root)
        {
            HashSet<LineKey> lines = [];
            Collect(root, inEvaluation: false, lines, isRoot: true);
            return lines;
        }

        /// <summary>
        /// Walks the content in order, tracking expression evaluation: text there (string literals,
        /// <c>VAR</c> values, choice-only text) is data, not output, so it isn't a line to reach.
        /// </summary>
        private void Collect(Ink.Runtime.Container container, bool inEvaluation, HashSet<LineKey> lines, bool isRoot = false)
        {
            foreach (var obj in container.content)
            {
                switch (obj)
                {
                    case Ink.Runtime.ControlCommand { commandType: Ink.Runtime.ControlCommand.CommandType.EvalStart }:
                        inEvaluation = true;
                        break;
                    case Ink.Runtime.ControlCommand { commandType: Ink.Runtime.ControlCommand.CommandType.EvalEnd }:
                        inEvaluation = false;
                        break;
                    case Ink.Runtime.Container child:
                        Collect(child, inEvaluation, lines);
                        break;
                    case Ink.Runtime.StringValue { value: var text } when !inEvaluation && !string.IsNullOrWhiteSpace(text):
                        Add(obj, lines);
                        break;
                    case Ink.Runtime.ChoicePoint { isInvisibleDefault: false } choicePoint when KeyOf(obj) is { } key:
                        lines.Add(key);
                        _choiceLines[choicePoint.path.ToString()] = key;
                        break;
                }
            }

            // Knots, stitches and the like that are only reached by name: each starts outside evaluation.
            foreach (var (name, named) in container.namedOnlyContent ?? [])
            {
                // The global declarations run once at start, as evaluation only.
                if (named is Ink.Runtime.Container child && !(isRoot && name == "global decl"))
                {
                    Collect(child, inEvaluation: false, lines);
                }
            }
        }

        public void MarkOutput(List<Ink.Runtime.Object> output, HashSet<LineKey> reached)
        {
            foreach (var obj in output)
            {
                if (obj is Ink.Runtime.StringValue)
                {
                    Add(obj, reached);
                }
            }
        }

        public void MarkChoice(string? sourcePath, HashSet<LineKey> reached)
        {
            if (sourcePath is not null && _choiceLines.TryGetValue(sourcePath, out var key))
            {
                reached.Add(key);
            }
        }

        private void Add(Ink.Runtime.Object obj, HashSet<LineKey> lines)
        {
            if (KeyOf(obj) is { } key)
            {
                lines.Add(key);
            }
        }

        private LineKey? KeyOf(Ink.Runtime.Object obj) =>
            obj.debugMetadata is { startLineNumber: > 0 } metadata && snapshot.ResolveFileName(metadata.fileName) is { } file
                ? new LineKey(file, metadata.startLineNumber)
                : null;
    }
}
