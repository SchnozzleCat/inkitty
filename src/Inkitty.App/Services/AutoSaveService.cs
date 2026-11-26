using Inkitty.App.ViewModels;
using Inkitty.Core.Projects;
using Inkitty.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Inkitty.App.Services;

/// <summary>Applies the <see cref="AutoSaveMode"/> setting to the current project.</summary>
public sealed class AutoSaveService : IDisposable
{
    private readonly ISettingsStore _settings;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Dictionary<InkSourceFile, ITimer> _timers = [];
    private ProjectViewModel? _project;

    public AutoSaveService(ISettingsStore settings, IUiDispatcher dispatcher, ILogger<AutoSaveService> logger, TimeProvider? time = null)
    {
        _settings = settings;
        _dispatcher = dispatcher;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _settings.Changed += OnSettingsChanged;
    }

    public AutoSaveMode Mode => _settings.Current.AutoSave;

    /// <summary>Raised after an automatic save, with the number of files saved.</summary>
    public event EventHandler<int>? Saved;

    /// <summary>Raised when an automatic save fails (e.g. the file is read-only).</summary>
    public event EventHandler<Exception>? Failed;

    public void Attach(ProjectViewModel? project)
    {
        CancelTimers();
        _project = project;
    }

    /// <summary>Called by the shell on every buffer edit.</summary>
    public void OnEdited(InkSourceFile file)
    {
        if (Mode != AutoSaveMode.AfterDelay || _project is null)
        {
            return;
        }

        if (_timers.Remove(file, out var existing))
        {
            existing.Dispose();
        }

        var delay = TimeSpan.FromMilliseconds(_settings.Current.AutoSaveDelayMs);
        _timers[file] = _time.CreateTimer(_ => _dispatcher.Post(() => _ = SaveFileAsync(file)), null, delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Called by the shell when the window deactivates or the active document changes.</summary>
    public async Task OnFocusLostAsync()
    {
        if (Mode != AutoSaveMode.OnFocusLost || _project is null)
        {
            return;
        }

        try
        {
            var count = await _project.SaveAllAsync(includeConflicted: false);
            if (count > 0)
            {
                Saved?.Invoke(this, count);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Auto-save failed");
            Failed?.Invoke(this, ex);
        }
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChanged;
        CancelTimers();
    }

    private async Task SaveFileAsync(InkSourceFile file)
    {
        if (_timers.Remove(file, out var timer))
        {
            timer.Dispose();
        }

        // Never overwrite an external change the user hasn't resolved yet.
        if (_project is null || !file.IsDirty || file.IsInConflict || _project.Project.Find(file.RelativePath) != file)
        {
            return;
        }

        try
        {
            await _project.SaveAsync(file);
            Saved?.Invoke(this, 1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Auto-save of {File} failed", file.RelativePath);
            Failed?.Invoke(this, ex);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (settings.AutoSave != AutoSaveMode.AfterDelay)
        {
            _dispatcher.Post(CancelTimers);
        }
    }

    private void CancelTimers()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Dispose();
        }

        _timers.Clear();
    }
}
