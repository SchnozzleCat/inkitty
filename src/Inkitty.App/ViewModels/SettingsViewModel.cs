using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Inkitty.App.Theming;
using Inkitty.Core.Settings;

namespace Inkitty.App.ViewModels;

/// <summary>Backs the settings window. Changes apply immediately and are persisted with a debounce.</summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store;
    private bool _loading;

    public SettingsViewModel(ISettingsStore store)
    {
        _store = store;
        Load(store.Current);
    }

    public IReadOnlyList<AutoSaveMode> AutoSaveModes { get; } = Enum.GetValues<AutoSaveMode>();

    /// <summary>"System" followed by every built-in theme.</summary>
    public IReadOnlyList<ThemeChoice> Themes { get; } =
    [
        new(ThemeCatalog.SystemId, "System (Light+ / Dark+)", null),
        .. ThemeCatalog.Themes.Select(t => new ThemeChoice(t.Id, t.DisplayName, t)),
    ];

    [ObservableProperty]
    public partial AutoSaveMode AutoSave { get; set; }

    [ObservableProperty]
    public partial decimal AutoSaveDelayMs { get; set; }

    [ObservableProperty]
    public partial ThemeChoice? Theme { get; set; }

    [ObservableProperty]
    public partial string EditorFontFamily { get; set; } = string.Empty;

    [ObservableProperty]
    public partial decimal EditorFontSize { get; set; }

    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    [ObservableProperty]
    public partial decimal CompileDebounceMs { get; set; }

    [ObservableProperty]
    public partial decimal ReplayTimeBudgetMs { get; set; }

    [ObservableProperty]
    public partial bool AltClickNavigates { get; set; }

    public bool IsAutoSaveDelayEnabled => AutoSave == AutoSaveMode.AfterDelay;

#pragma warning disable CA1822 // bound from XAML, must be an instance member
    public string AltClickLabel => OperatingSystem.IsMacOS() ? "Option+click goes to definition" : "Alt+click goes to definition";
#pragma warning restore CA1822

    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaults = new AppSettings();
        _store.Update(s => defaults with { RecentProjects = s.RecentProjects, LastWindowBounds = s.LastWindowBounds, KeyBindings = s.KeyBindings });
        Load(_store.Current);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(AutoSave))
        {
            OnPropertyChanged(nameof(IsAutoSaveDelayEnabled));
        }

        if (_loading || e.PropertyName is nameof(IsAutoSaveDelayEnabled) or nameof(AltClickLabel))
        {
            return;
        }

        _store.Update(s => s with
        {
            AutoSave = AutoSave,
            AutoSaveDelayMs = (int)AutoSaveDelayMs,
            ColorTheme = Theme?.Id ?? ThemeCatalog.SystemId,
            EditorFontFamily = string.IsNullOrWhiteSpace(EditorFontFamily) ? AppSettings.DefaultEditorFont : EditorFontFamily,
            EditorFontSize = (double)EditorFontSize,
            WordWrap = WordWrap,
            CompileDebounceMs = (int)CompileDebounceMs,
            ReplayTimeBudgetMs = (int)ReplayTimeBudgetMs,
            AltClickNavigates = AltClickNavigates,
        });
    }

    private void Load(AppSettings s)
    {
        _loading = true;
        try
        {
            AutoSave = s.AutoSave;
            AutoSaveDelayMs = s.AutoSaveDelayMs;
            Theme = Themes.FirstOrDefault(t => t.Id == s.ColorTheme) ?? Themes[0];
            EditorFontFamily = s.EditorFontFamily;
            EditorFontSize = (decimal)s.EditorFontSize;
            WordWrap = s.WordWrap;
            CompileDebounceMs = s.CompileDebounceMs;
            ReplayTimeBudgetMs = s.ReplayTimeBudgetMs;
            AltClickNavigates = s.AltClickNavigates;
        }
        finally
        {
            _loading = false;
        }
    }
}

/// <summary>An entry in the theme picker.</summary>
public sealed record ThemeChoice(string Id, string DisplayName, ThemeDefinition? Theme)
{
    public bool IsDark => Theme?.IsDark ?? false;

    public string Kind => Theme is null ? "follows OS" : Theme.IsDark ? "dark" : "light";

    /// <summary>Swatches for the picker: background, sidebar, accent, foreground.</summary>
    public IReadOnlyList<Avalonia.Media.IBrush> Swatches => Theme is { Colors: var c }
        ? [new Avalonia.Media.SolidColorBrush(c.Background), new Avalonia.Media.SolidColorBrush(c.Sidebar), new Avalonia.Media.SolidColorBrush(c.Accent), new Avalonia.Media.SolidColorBrush(c.Foreground)]
        : [new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFFFFF")), new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1E1E1E"))];

    public override string ToString() => DisplayName;
}
