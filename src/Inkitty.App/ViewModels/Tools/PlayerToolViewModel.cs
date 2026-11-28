using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Services;
using Inkitty.Core.Compilation;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Playback;
using Inkitty.Core.Settings;
using Inkitty.Core.StoryMap;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>
/// "Play as you write": after every successful compile the story is replayed along the recorded
/// choices, off the UI thread, and the transcript is swapped in one go.
/// </summary>
public sealed partial class PlayerToolViewModel : Tool, IDisposable
{
    private readonly IUiDispatcher _dispatcher;
    private readonly ISettingsStore _settings;
    private readonly TimeProvider _time;
    private ProjectViewModel? _project;
    private PlaySession? _session;
    private Task _sessionWork = Task.CompletedTask;
    private ChoiceHistory _history = ChoiceHistory.Empty;
    private List<Issue> _runtimeIssues = [];
    private CancellationTokenSource? _replayCts;
    private int _replayGeneration;
    private List<StoryEvent> _events = [];
    private CancellationTokenSource? _reachabilityCts;

    /// <summary>
    /// Seed of the story's randomness. Kept across replays (edits, step back) so random text and choices
    /// don't change under the reader; Restart picks a new one unless <see cref="KeepSeed"/> is on.
    /// </summary>
    private int _seed = PlaySession.NewSeed();

    public PlayerToolViewModel(IUiDispatcher dispatcher, ISettingsStore settings, TimeProvider? time = null)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _time = time ?? TimeProvider.System;
        Id = DockIds.Player;
        Title = "Player";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
        KeepSeed = settings.Current.KeepStorySeed;
        settings.Changed += (_, _) => _dispatcher.Post(() =>
        {
            KeepSeed = _settings.Current.KeepStorySeed; // e.g. Settings > Reset to defaults (latest value, not the posted one)
            OnPropertyChanged(nameof(ReachabilityToolTip));
            OnPropertyChanged(nameof(ReachabilityLabel));
        });
    }

    public ObservableCollection<PlayerItem> Items { get; } = [];

    public ObservableCollection<PlayerChoice> Choices { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>True while the story has compile errors: the last good run (if any) is shown dimmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StaleMessage))]
    public partial bool IsStale { get; private set; }

    public string StaleMessage => Items.Count > 0 || Choices.Count > 0
        ? "Story has errors — showing the last successful run"
        : "Story has errors — fix them to play it (see Issues)";

    [ObservableProperty]
    public partial bool HasProject { get; private set; }

    /// <summary>The graph of the story being played (from the same compile as the session).</summary>
    [ObservableProperty]
    public partial StoryGraph Graph { get; private set; } = StoryGraph.Empty;

    /// <summary>Where the playthrough is and how it got there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationText), nameof(HasLocation))]
    public partial StoryProgress Progress { get; private set; } = StoryProgress.Empty;

    /// <summary>Every global variable at the current point of the playthrough.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<StoryVariable> Variables { get; private set; } = [];

    /// <summary>Breadcrumb for the current knot/stitch, e.g. <c>town › square</c>.</summary>
    public string LocationText => Progress.CurrentNode switch
    {
        null => string.Empty,
        StoryGraph.StartId => "Beginning",
        StoryGraph.EndId => "The end",
        var id => id.Replace(".", " › ", StringComparison.Ordinal),
    };

    public bool HasLocation => Progress.CurrentNode is not null;

    /// <summary>Raised when the user asks to see where a transcript line was written.</summary>
    public event EventHandler<StoryLocation>? RevealRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StepBackCommand))]
    public partial int ChoiceCount { get; private set; }

    /// <summary>Restart with the same seed (persisted), so random text and choices come out the same.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeedToolTip))]
    public partial bool KeepSeed { get; set; }

    partial void OnKeepSeedChanged(bool value)
    {
        if (_settings.Current.KeepStorySeed != value)
        {
            _settings.Update(s => s with { KeepStorySeed = value });
        }
    }

    /// <summary>The seed of the story's randomness, for the tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeedToolTip))]
    public partial int Seed { get; private set; }

    public string SeedToolTip => KeepSeed
        ? $"Restart keeps the seed ({Seed}), so random text and choices come out the same"
        : $"Restart picks a new seed (now {Seed}). Turn on to replay the same random text and choices";

    /// <summary>True while a reachability check is running.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReachabilityToolTip), nameof(ReachabilityLabel))]
    public partial bool IsCheckingReachability { get; private set; }

    /// <summary>Playthroughs finished by the running reachability check.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReachabilityToolTip), nameof(ReachabilityLabel))]
    public partial int ReachabilityRunsDone { get; private set; }

    public string ReachabilityLabel => IsCheckingReachability
        ? $"Stop ({ReachabilityRunsDone}/{_settings.Current.ReachabilityRuns})"
        : "Reachable";

    public string ReachabilityToolTip => IsCheckingReachability
        ? $"Checking… {ReachabilityRunsDone} of {_settings.Current.ReachabilityRuns} playthroughs. Click to stop"
        : $"Play the story {_settings.Current.ReachabilityRuns} times with random choices and warn about every line that was never reached";

    /// <summary>Raised after the transcript changes, so the view can scroll to the end.</summary>
    public event EventHandler? TranscriptUpdated;

    public void Attach(ProjectViewModel? project)
    {
        CancelReachabilityCheck();
        if (_project is not null)
        {
            _project.Compiled -= OnCompiled;
            _project.Edited -= OnProjectEdited;
        }

        _project = project;
        HasProject = project is not null;
        _history = ChoiceHistory.Empty;
        ChoiceCount = 0;
        if (!KeepSeed)
        {
            _seed = PlaySession.NewSeed();
        }

        ReleaseSession();
        Items.Clear();
        Choices.Clear();
        IsStale = false;
        _events = [];
        Graph = StoryGraph.Empty;
        Progress = StoryProgress.Empty;
        Variables = [];

        if (project is not null)
        {
            project.Compiled += OnCompiled;
            project.Edited += OnProjectEdited;
            if (project.LastSuccess is { } success)
            {
                StartReplay(success);
            }
        }
    }

    [RelayCommand]
    private void Restart()
    {
        if (!KeepSeed)
        {
            _seed = PlaySession.NewSeed();
        }

        _history = ChoiceHistory.Empty;
        ChoiceCount = 0;
        if (_project?.LastSuccess is { } success)
        {
            StartReplay(success);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStepBack))]
    private void StepBack()
    {
        _history = _history.RemoveLast();
        ChoiceCount = _history.Count;
        if (_project?.LastSuccess is { } success)
        {
            StartReplay(success);
        }
    }

    private bool CanStepBack() => ChoiceCount > 0;

    /// <summary>
    /// Plays the story many times with automatic choices and reports every line no playthrough
    /// reached as a warning. Clicking again while it runs stops it. Any edit stops it too, and clears
    /// the warnings, since reachability may then have changed.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)] // a second click while running stops it
    private async Task CheckReachabilityAsync()
    {
        if (IsCheckingReachability)
        {
            CancelReachabilityCheck();
            return;
        }

        if (_project is not { } project)
        {
            return;
        }

        if (project.LastSuccess is not { } success || !project.IsCurrent(success.Snapshot))
        {
            project.SendStatus(project.LastResult?.HasErrors == true
                ? "Fix the story's errors before checking reachability"
                : "The story is still compiling; try again in a moment");
            return;
        }

        var cts = _reachabilityCts = new CancellationTokenSource();
        var settings = _settings.Current;
        var options = new ReachabilityOptions(settings.ReachabilityRuns, RunTimeBudget: TimeSpan.FromMilliseconds(settings.ReplayTimeBudgetMs));
        ReachabilityRunsDone = 0;
        IsCheckingReachability = true;
        try
        {
            var report = await Task.Run(
                () => ReachabilityChecker.CheckAsync(
                    success.Story,
                    success.Snapshot,
                    options,
                    runs => _dispatcher.Post(() =>
                    {
                        if (!cts.IsCancellationRequested)
                        {
                            ReachabilityRunsDone = runs;
                        }
                    }),
                    _time,
                    cts.Token),
                cts.Token);

            if (!cts.IsCancellationRequested && ReferenceEquals(project, _project))
            {
                project.SetReachabilityResult(success.Snapshot, report);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped, or the story was edited.
        }
        catch (Exception ex)
        {
            project.SendStatus($"Reachability check failed: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_reachabilityCts, cts))
            {
                _reachabilityCts = null;
                IsCheckingReachability = false;
            }

            cts.Dispose();
        }
    }

    private void CancelReachabilityCheck()
    {
        if (_reachabilityCts is { } cts)
        {
            _reachabilityCts = null;
            cts.Cancel();
            IsCheckingReachability = false;
        }
    }

    private void OnProjectEdited(object? sender, EventArgs e) => CancelReachabilityCheck();

    /// <summary>Jumps the editor to where a transcript line (or, without a parameter, the current position) was written.</summary>
    [RelayCommand]
    private void Reveal(StoryLocation? location)
    {
        if ((location ?? Progress.Current) is { File: not null } target)
        {
            RevealRequested?.Invoke(this, target);
        }
    }

    [RelayCommand]
    private void Choose(PlayerChoice choice)
    {
        if (_session is null || IsBusy)
        {
            return;
        }

        var session = _session;
        var budget = Budget();
        var generation = ++_replayGeneration;
        var graph = Graph;
        var previous = _events;
        IsBusy = true;
        _sessionWork = Task.Run(() =>
        {
            List<StoryEvent> events = [];
            try
            {
                events.Add(session.Choose(choice.Index));
                events.AddRange(session.ContinueUntilChoice(budget));
            }
            catch (StoryBudgetExceededException ex)
            {
                events.Add(RuntimeError(ex));
                events.Add(new StoryEnded());
            }
            catch (Exception ex)
            {
                // Not only StoryException: ink's runtime also throws plain .NET exceptions.
                events.Add(RuntimeError(ex));
                events.Add(new StoryEnded());
            }

            List<StoryEvent> all = [.. previous, .. events];
            StoryProgress progress;
            IReadOnlyList<StoryVariable> variables;
            try
            {
                progress = StoryProgress.Build(graph, all, session.VisitCount);
                variables = session.Variables();
            }
            catch (Exception ex)
            {
                PostFailure(generation, ex);
                return;
            }

            _dispatcher.Post(() =>
            {
                if (generation != _replayGeneration)
                {
                    return;
                }

                _events = all;
                Progress = progress;
                Variables = variables;
                _project?.SetProgress(progress);
                _history = session.History;
                ChoiceCount = _history.Count;
                Choices.Clear();
                var (items, choices) = PlayerItemMapper.Map(events, newFromIndex: 0);
                foreach (var item in items)
                {
                    Items.Add(item);
                }

                foreach (var c in choices)
                {
                    Choices.Add(c);
                }

                ReportRuntimeIssues(events, append: true);
                IsBusy = false;
                TranscriptUpdated?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private void OnCompiled(object? sender, CompileResult result)
    {
        switch (result)
        {
            case CompileSucceeded success:
                IsStale = false;
                StartReplay(success);
                break;
            case CompileFailed:
                IsStale = true;
                OnPropertyChanged(nameof(StaleMessage));
                break;
        }
    }

    private PlaybackBudget Budget() => new(TimeSpan.FromMilliseconds(_settings.Current.ReplayTimeBudgetMs));

    private void StartReplay(CompileSucceeded success)
    {
        _replayCts?.Cancel();
        _replayCts?.Dispose();
        var cts = _replayCts = new CancellationTokenSource();
        var generation = ++_replayGeneration;
        var history = _history;
        var seed = _seed;
        var budget = Budget();
        IsBusy = true;
        Seed = seed;

        _ = Task.Run(() =>
        {
            // A compiled Story can only be played by one session at a time; a fresh compile gives a
            // fresh Story per generation, so the previous session is simply abandoned.
            PlaySession? session = null;
            ReplayResult result;
            StoryProgress progress;
            IReadOnlyList<StoryVariable> variables;
            try
            {
                session = new PlaySession(success.Story, success.Snapshot, _time, seed);
                result = ReplayEngine.Replay(session, history, budget, _time, cts.Token);
                progress = StoryProgress.Build(success.Graph, result.Events, session.VisitCount);
                variables = session.Variables();
            }
            catch (OperationCanceledException)
            {
                session?.Dispose();
                return;
            }
            catch (Exception ex)
            {
                session?.Dispose();
                PostFailure(generation, ex);
                return;
            }

            _dispatcher.Post(() =>
            {
                if (generation != _replayGeneration || cts.IsCancellationRequested)
                {
                    session.Dispose();
                    return;
                }

                ReleaseSession();
                _session = session;
                _events = [.. result.Events];
                Graph = success.Graph;
                Progress = progress;
                Variables = variables;
                _project?.SetProgress(progress);
                _history = session.History;
                ChoiceCount = _history.Count;

                var (items, choices) = PlayerItemMapper.Map(result.Events, NewContentStart(result));
                Items.Clear();
                foreach (var item in items)
                {
                    Items.Add(item);
                }

                Choices.Clear();
                foreach (var choice in choices)
                {
                    Choices.Add(choice);
                }

                ReportRuntimeIssues(result.Events, append: false);
                IsBusy = false;
                TranscriptUpdated?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private static RuntimeIssue RuntimeError(Exception ex) =>
        new(new Issue(IssueSeverity.RuntimeError, ex.Message, null, IssueOrigin.Runtime));

    /// <summary>
    /// A background run failed outside the story itself: report it and leave the busy state, so the
    /// player doesn't stay stuck waiting for a result that will never come.
    /// </summary>
    private void PostFailure(int generation, Exception ex) =>
        _dispatcher.Post(() =>
        {
            if (generation != _replayGeneration)
            {
                return;
            }

            ReportRuntimeIssues([RuntimeError(ex)], append: true);
            IsBusy = false;
        });

    /// <summary>Events after the last replayed choice are highlighted as new (everything, before any choice).</summary>
    private static int NewContentStart(ReplayResult result)
    {
        for (var i = result.Events.Count - 1; i >= 0; i--)
        {
            if (result.Events[i].Is<ChoiceTaken>())
            {
                return i + 1;
            }
        }

        return 0;
    }

    public void Dispose()
    {
        CancelReachabilityCheck();
        _replayCts?.Cancel();
        _replayCts?.Dispose();
        ReleaseSession();
    }

    /// <summary>Disposes the current session once any background choice running on it has finished.</summary>
    private void ReleaseSession()
    {
        if (_session is { } old)
        {
            _session = null;
            _sessionWork.ContinueWith(_ => old.Dispose(), TaskScheduler.Default);
        }

        _sessionWork = Task.CompletedTask;
    }

    private void ReportRuntimeIssues(IEnumerable<StoryEvent> events, bool append)
    {
        var issues = events.OfCase<RuntimeIssue>().Select(i => i.Issue);
        _runtimeIssues = append ? [.. _runtimeIssues, .. issues] : [.. issues];
        _project?.SetRuntimeIssues(_runtimeIssues);
    }
}
