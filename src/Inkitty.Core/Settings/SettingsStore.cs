using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Inkitty.Core.Settings;

public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    void Update(Func<AppSettings, AppSettings> change);

    Task FlushAsync();
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Writes are debounced; a corrupt file is moved
/// aside to <c>settings.json.bak</c> and defaults are used.
/// </summary>
public sealed class SettingsStore : ISettingsStore, IAsyncDisposable
{
    public const string FileName = "settings.json";

    private readonly string _path;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TimeSpan _debounce;
    private readonly Lock _gate = new();
    private ITimer? _pendingSave;

    public SettingsStore(string directory, TimeProvider? time = null, ILogger<SettingsStore>? logger = null, TimeSpan? debounce = null)
    {
        _path = Path.Join(directory, FileName);
        _time = time ?? TimeProvider.System;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);
        Current = Load();
    }

    public string FilePath => _path;

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? Changed;

    public void Update(Func<AppSettings, AppSettings> change)
    {
        AppSettings updated;
        lock (_gate)
        {
            updated = change(Current);
            if (updated == Current)
            {
                return;
            }

            Current = updated;
            _pendingSave?.Dispose();
            _pendingSave = _time.CreateTimer(_ => _ = SaveNowAsync(), null, _debounce, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke(this, updated);
    }

    public async Task FlushAsync()
    {
        bool pending;
        lock (_gate)
        {
            pending = _pendingSave is not null;
            _pendingSave?.Dispose();
            _pendingSave = null;
        }

        if (pending)
        {
            await SaveNowAsync();
        }
    }

    public ValueTask DisposeAsync() => new(FlushAsync());

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            _logger.LogWarning(ex, "Settings file {Path} is corrupt; using defaults", _path);
            try
            {
                File.Move(_path, _path + ".bak", overwrite: true);
            }
            catch (IOException)
            {
                // Best effort only.
            }

            return new AppSettings();
        }
    }

    private async Task SaveNowAsync()
    {
        AppSettings snapshot;
        lock (_gate)
        {
            snapshot = Current;
            _pendingSave?.Dispose();
            _pendingSave = null;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SettingsJsonContext.Default.AppSettings);
            }

            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings to {Path}", _path);
        }
    }
}
