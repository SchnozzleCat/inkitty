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
    }

    public ObservableCollection<PlayerItem> Items { get; } = [];

    public ObservableCollection<PlayerChoice> Choices { get; } = [];

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>True while the story has compile errors: the last good run is shown dimmed.</summary>
    [ObservableProperty]
    public partial bool IsStale { get; private set; }

    [ObservableProperty]
    public partial bool HasProject { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StepBackCommand))]
    public partial int ChoiceCount { get; private set; }

    /// <summary>Raised after the transcript changes, so the view can scroll to the end.</summary>
    public event EventHandler? TranscriptUpdated;

    public void Attach(ProjectViewModel? project)
    {
        if (_project is not null)
        {
            _project.Compiled -= OnCompiled;
        }

        _project = project;
        HasProject = project is not null;
        _history = ChoiceHistory.Empty;
        ChoiceCount = 0;
        ReleaseSession();
        Items.Clear();
        Choices.Clear();
        IsStale = false;

        if (project is not null)
        {
            project.Compiled += OnCompiled;
            if (project.LastSuccess is { } success)
            {
                StartReplay(success);
            }
        }
    }

    [RelayCommand]
    private void Restart()
    {
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
                events.Add(new RuntimeIssue(new Issue(IssueSeverity.RuntimeError, ex.Message, null, IssueOrigin.Runtime)));
                events.Add(new StoryEnded());
            }
            catch (Exception ex) when (ex is Ink.Runtime.StoryException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                events.Add(new RuntimeIssue(new Issue(IssueSeverity.RuntimeError, ex.Message, null, IssueOrigin.Runtime)));
            }

            _dispatcher.Post(() =>
            {
                if (generation != _replayGeneration)
                {
                    return;
                }

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
                IsStale = Items.Count > 0 || Choices.Count > 0;
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
        var budget = Budget();
        IsBusy = true;

        _ = Task.Run(() =>
        {
            // A compiled Story can only be played by one session at a time; a fresh compile gives a
            // fresh Story per generation, so the previous session is simply abandoned.
            var session = new PlaySession(success.Story, success.Snapshot, _time);
            ReplayResult result;
            try
            {
                result = ReplayEngine.Replay(session, history, budget, _time, cts.Token);
            }
            catch (OperationCanceledException)
            {
                session.Dispose();
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

    /// <summary>Events after the last replayed choice are highlighted as new (only once the player has made choices).</summary>
    private static int NewContentStart(ReplayResult result)
    {
        if (result.ReplayedChoices == 0)
        {
            return int.MaxValue;
        }

        for (var i = result.Events.Count - 1; i >= 0; i--)
        {
            if (result.Events[i].Is<ChoiceTaken>())
            {
                return i + 1;
            }
        }

        return int.MaxValue;
    }

    public void Dispose()
    {
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
