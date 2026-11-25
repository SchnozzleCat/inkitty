using Avalonia;
using Avalonia.Styling;
using Inkitty.Core.Settings;

namespace Inkitty.App.Services;

/// <summary>Maps the theme setting onto Avalonia's requested theme variant.</summary>
public sealed class ThemeService
{
    private readonly ISettingsStore _settings;

    public ThemeService(ISettingsStore settings)
    {
        _settings = settings;
        _settings.Changed += (_, s) => Apply(s.Theme);
    }

    public void ApplyCurrent() => Apply(_settings.Current.Theme);

    private static void Apply(ThemePreference theme)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        app.RequestedThemeVariant = theme switch
        {
            ThemePreference.Light => ThemeVariant.Light,
            ThemePreference.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }
}
