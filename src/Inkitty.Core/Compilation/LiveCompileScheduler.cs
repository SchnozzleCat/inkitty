using System.Threading.Channels;
using Inkitty.Core.Projects;

namespace Inkitty.Core.Compilation;

/// <summary>
/// Turns a stream of "something changed" notifications into compilations:
/// debounced, latest-wins, one at a time, with stale results discarded by generation.
/// </summary>
public sealed class LiveCompileScheduler : IAsyncDisposable
{
    private readonly IInkCompilerService _compiler;
    private readonly Func<CancellationToken, Task<ProjectSnapshot>> _snapshotProvider;
    private readonly TimeProvider _time;
    private readonly Channel<int> _signals = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
    });

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _gate = new();
    private readonly Task _worker;
    private ITimer? _debounceTimer;
    private int _requested;
    private int _busy;

    /// <param name="snapshotProvider">
    /// Captures the project state for a compile. Called on the worker; implementations that need the UI
    /// thread (e.g. to refresh includes and copy buffers) should dispatch themselves.
    /// </param>
    public LiveCompileScheduler(
        IInkCompilerService compiler,
        Func<CancellationToken, Task<ProjectSnapshot>> snapshotProvider,
        TimeSpan debounce,
        TimeProvider? time = null)
    {
        _compiler = compiler;
        _snapshotProvider = snapshotProvider;
        _time = time ?? TimeProvider.System;
        Debounce = debounce;
        _worker = Task.Run(RunAsync);
    }

    public TimeSpan Debounce { get; set; }

    /// <summary>Generation of the most recent request.</summary>
    public int LatestGeneration => Volatile.Read(ref _requested);

    public bool IsBusy => Volatile.Read(ref _busy) > 0;

    /// <summary>Raised (on a background thread) with each compile result that is still current.</summary>
    public event EventHandler<CompileResult>? Compiled;

    /// <summary>Raised (on a background thread) when <see cref="IsBusy"/> changes.</summary>
    public event EventHandler<bool>? BusyChanged;

    /// <summary>Raised (on a background thread) if the snapshot provider or compiler throws unexpectedly.</summary>
    public event EventHandler<Exception>? Faulted;

    /// <summary>Requests a compile after the debounce interval; restarts the interval if one is pending.</summary>
    public void Request()
    {
        lock (_gate)
        {
            var generation = ++_requested;
            _debounceTimer?.Dispose();
            _debounceTimer = _time.CreateTimer(_ => Signal(generation), null, Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Requests a compile immediately (e.g. on save, open or restart).</summary>
    public void RequestNow()
    {
        int generation;
        lock (_gate)
        {
            generation = ++_requested;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        Signal(generation);
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        _signals.Writer.TryComplete();
        await _shutdown.CancelAsync();
        try
        {
            await _worker;
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }

    private void Signal(int generation) => _signals.Writer.TryWrite(generation);

    private bool IsCurrent(int generation) => generation == Volatile.Read(ref _requested);

    private async Task RunAsync()
    {
        var token = _shutdown.Token;
        await foreach (var generation in _signals.Reader.ReadAllAsync(token))
        {
            if (!IsCurrent(generation))
            {
                continue; // a newer request is pending; it will signal when ready
            }

            SetBusy(true);
            try
            {
                var snapshot = await _snapshotProvider(token);
                if (!IsCurrent(generation))
                {
                    continue;
                }

                var result = _compiler.Compile(snapshot, generation, token);
                if (IsCurrent(generation))
                {
                    Compiled?.Invoke(this, result);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(this, ex);
            }
            finally
            {
                SetBusy(false);
            }
        }
    }

    private void SetBusy(bool busy)
    {
        var now = busy ? Interlocked.Increment(ref _busy) : Interlocked.Decrement(ref _busy);
        if (busy ? now == 1 : now == 0)
        {
            BusyChanged?.Invoke(this, busy);
        }
    }
}
