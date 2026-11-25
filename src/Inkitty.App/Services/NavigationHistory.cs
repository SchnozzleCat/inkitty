using Inkitty.Core.Symbols;

namespace Inkitty.App.Services;

/// <summary>A place in a file the user was at.</summary>
public readonly record struct NavigationLocation(string File, SourcePosition Position);

/// <summary>Back/forward stack for jump-to-definition and issue navigation.</summary>
public sealed class NavigationHistory
{
    private const int MaxEntries = 100;
    private readonly List<NavigationLocation> _back = [];
    private readonly List<NavigationLocation> _forward = [];

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    public event EventHandler? Changed;

    /// <summary>Records where the user was before jumping somewhere else.</summary>
    public void Push(NavigationLocation from)
    {
        if (_back.Count > 0 && _back[^1] == from)
        {
            return;
        }

        _back.Add(from);
        if (_back.Count > MaxEntries)
        {
            _back.RemoveAt(0);
        }

        _forward.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public NavigationLocation? GoBack(NavigationLocation current) => Move(_back, _forward, current);

    public NavigationLocation? GoForward(NavigationLocation current) => Move(_forward, _back, current);

    public void Clear()
    {
        _back.Clear();
        _forward.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private NavigationLocation? Move(List<NavigationLocation> from, List<NavigationLocation> to, NavigationLocation current)
    {
        if (from.Count == 0)
        {
            return null;
        }

        var target = from[^1];
        from.RemoveAt(from.Count - 1);
        to.Add(current);
        Changed?.Invoke(this, EventArgs.Empty);
        return target;
    }
}
