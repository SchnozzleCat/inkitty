using Avalonia.Input;
using Inkitty.Core.Settings;

namespace Inkitty.App.Input;

/// <summary>
/// The keyboard shortcuts in effect (defaults plus the user's changes in <see cref="AppSettings.KeyBindings"/>),
/// and changing them. The views bind the shortcuts and label the menus from <see cref="Gestures"/>.
/// </summary>
public sealed class ShortcutService
{
    private readonly ISettingsStore _settings;
    private IReadOnlyDictionary<string, string> _resolvedFrom;
    private int _suspended;

    public ShortcutService(ISettingsStore settings, ShortcutPlatform? platform = null)
    {
        _settings = settings;
        Platform = platform ?? ShortcutPlatform.Current;
        _resolvedFrom = settings.Current.KeyBindings;
        Gestures = ShortcutCatalog.Resolve(settings.Current, Platform);
        _settings.Changed += (_, s) =>
        {
            if (!ReferenceEquals(s.KeyBindings, _resolvedFrom))
            {
                _resolvedFrom = s.KeyBindings;
                Gestures = ShortcutCatalog.Resolve(s, Platform);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public ShortcutPlatform Platform { get; }

    /// <summary>Command id to its shortcut (null: none).</summary>
    public IReadOnlyDictionary<string, KeyGesture?> Gestures { get; private set; }

    /// <summary>
    /// True while the shortcuts editor is recording a key: the shortcuts are unbound so the key reaches
    /// the editor instead of running a command.
    /// </summary>
    public bool IsSuspended => _suspended > 0;

    /// <summary>Raised (on the caller's thread) when the shortcuts change or are suspended/resumed.</summary>
    public event EventHandler? Changed;

    public KeyGesture? GestureFor(string id) => Gestures.GetValueOrDefault(id);

    public string DisplayText(string id) => ShortcutCatalog.Format(GestureFor(id), Platform);

    public KeyGesture? DefaultFor(string id) =>
        ShortcutCatalog.Find(id) is { } command && ShortcutCatalog.TryParse(command.DefaultFor(Platform), Platform, out var gesture)
            ? gesture
            : null;

    public bool IsCustomised(string id) => !Equals(GestureFor(id), DefaultFor(id));

    /// <summary>The other commands with the same shortcut as <paramref name="id"/>.</summary>
    public IReadOnlyList<ShortcutCommand> ConflictsWith(string id) =>
        GestureFor(id) is { } gesture
            ? ShortcutCatalog.Commands.Where(c => c.Id != id && Equals(GestureFor(c.Id), gesture)).ToList()
            : [];

    /// <summary>Sets the shortcut for a command (null: none). Choosing the default clears the override.</summary>
    public void Set(string id, KeyGesture? gesture)
    {
        if (ShortcutCatalog.Find(id) is null)
        {
            throw new ArgumentException($"Unknown command '{id}'", nameof(id));
        }

        var matchesDefault = Equals(gesture, DefaultFor(id));
        _settings.Update(s =>
        {
            var bindings = new Dictionary<string, string>(s.KeyBindings);
            if (matchesDefault)
            {
                bindings.Remove(id);
            }
            else
            {
                bindings[id] = ShortcutCatalog.ToStored(gesture, Platform);
            }

            return s with { KeyBindings = bindings.Count == 0 ? AppSettings.NoKeyBindings : bindings };
        });
    }

    public void Reset(string id) => Set(id, DefaultFor(id));

    public void ResetAll() => _settings.Update(s => s with { KeyBindings = AppSettings.NoKeyBindings });

    /// <summary>Unbinds every shortcut until the returned handle is disposed (see <see cref="IsSuspended"/>).</summary>
    public IDisposable Suspend()
    {
        _suspended++;
        if (_suspended == 1)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return new Resumer(this);
    }

    private sealed class Resumer(ShortcutService service) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            service._suspended--;
            if (service._suspended == 0)
            {
                service.Changed?.Invoke(service, EventArgs.Empty);
            }
        }
    }
}
