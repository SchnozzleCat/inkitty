using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inkitty.Core.FileSystem;

/// <summary>
/// Watches a project folder for changes to <c>.ink</c> files. Raw events are coalesced per path
/// (editors emit several events per save; atomic saves appear as delete+create or rename).
/// Consumers decide whether a change is external by comparing content hashes.
/// </summary>
public sealed class ProjectWatcher : IAsyncDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly Channel<string> _raw = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TimeProvider _time;
    private readonly TimeSpan _coalesce;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _pump;

    public ProjectWatcher(string rootDirectory, TimeProvider? time = null, TimeSpan? coalesce = null, ILogger<ProjectWatcher>? logger = null)
    {
        RootDirectory = rootDirectory;
        _time = time ?? TimeProvider.System;
        _coalesce = coalesce ?? TimeSpan.FromMilliseconds(200);
        _logger = logger ?? (ILogger)NullLogger.Instance;

        try
        {
            _watcher = new FileSystemWatcher(rootDirectory, "*.ink")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.DirectoryName,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += (_, e) => Enqueue(e.FullPath);
            _watcher.Created += (_, e) => Enqueue(e.FullPath);
            _watcher.Deleted += (_, e) => Enqueue(e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                Enqueue(e.OldFullPath);
                Enqueue(e.FullPath);
            };
            _watcher.Error += (_, e) =>
            {
                _logger.LogWarning(e.GetException(), "File watcher error; requesting full rescan");
                Overflowed?.Invoke(this, EventArgs.Empty);
            };
            _watcher.EnableRaisingEvents = true;
            IsWatching = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            // e.g. inotify watch limit reached on Linux. Fall back to rescans on window focus.
            _logger.LogWarning(ex, "Could not watch {Root}; external changes are detected on window focus only", rootDirectory);
            _watcher?.Dispose();
            _watcher = null;
        }

        _pump = Task.Run(PumpAsync);
    }

    public string RootDirectory { get; }

    /// <summary>False if the OS watcher couldn't be started.</summary>
    public bool IsWatching { get; }

    /// <summary>Raised (on a background thread) with full paths of files that changed, after coalescing.</summary>
    public event EventHandler<IReadOnlyCollection<string>>? FilesChanged;

    /// <summary>Raised when events may have been lost; consumers should rescan everything.</summary>
    public event EventHandler? Overflowed;

    public async ValueTask DisposeAsync()
    {
        _watcher?.Dispose();
        _raw.Writer.TryComplete();
        await _shutdown.CancelAsync();
        try
        {
            await _pump;
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }

    private void Enqueue(string fullPath)
    {
        if (fullPath.EndsWith(".ink", PathComparer.Comparison) && !Path.GetFileName(fullPath).StartsWith('.'))
        {
            _raw.Writer.TryWrite(Path.GetFullPath(fullPath));
        }
    }

    private async Task PumpAsync()
    {
        var token = _shutdown.Token;
        var reader = _raw.Reader;
        while (await reader.WaitToReadAsync(token))
        {
            HashSet<string> batch = [with(PathComparer.Default)];
            while (reader.TryRead(out var first))
            {
                batch.Add(first);
            }

            // Keep collecting until things are quiet for the coalescing interval.
            while (true)
            {
                using var quiet = CancellationTokenSource.CreateLinkedTokenSource(token);
                var delay = Task.Delay(_coalesce, _time, quiet.Token);
                var more = reader.WaitToReadAsync(quiet.Token).AsTask();
                var finished = await Task.WhenAny(delay, more);
                if (finished == delay)
                {
                    await quiet.CancelAsync(); // stop waiting for more; the cancelled wait is discarded
                    break;
                }

                await quiet.CancelAsync();
                if (!await more)
                {
                    break; // channel completed
                }

                while (reader.TryRead(out var next))
                {
                    batch.Add(next);
                }
            }

            try
            {
                FilesChanged?.Invoke(this, batch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FilesChanged handler failed");
            }
        }
    }
}
