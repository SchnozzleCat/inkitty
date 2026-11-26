using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using Inkitty.App.ViewModels.Documents;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Settings;
using Inkitty.Core.Symbols;

namespace Inkitty.App.Controls.InkEditor;

/// <summary>
/// The ink editor: AvaloniaEdit with TextMate highlighting, issue squiggles and gutter, and
/// modifier+click / F12 go-to-definition. Binds to an <see cref="EditorDocumentViewModel"/>.
/// </summary>
public sealed class InkEditor : UserControl
{
    public static readonly StyledProperty<AppSettings?> SettingsProperty =
        AvaloniaProperty.Register<InkEditor, AppSettings?>(nameof(Settings));

    private readonly TextEditor _editor;
    private readonly IssueMarkerRenderer _markers = new();
    private readonly IssueGutterMargin _gutter;
    private readonly PlaythroughMargin _playthrough = new();
    private readonly ReferenceUnderlineColorizer _underline = new();
    private TextMate.Installation? _textMate;
    private ThemeName? _textMateTheme;
    private EditorDocumentViewModel? _viewModel;
    private Point? _lastPointer;
    private DispatcherTimer? _flashTimer;
    private AvaloniaEdit.Rendering.IBackgroundRenderer? _flash;

    public InkEditor()
    {
        _editor = new TextEditor
        {
            ShowLineNumbers = true,
            FontFamily = new FontFamily(AppSettings.DefaultEditorFont),
            FontSize = 14,
            WordWrap = true,
            Padding = new Thickness(4, 2),
        };
        _editor.Options.ConvertTabsToSpaces = false;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.HighlightCurrentLine = true;
        _editor.Options.AllowScrollBelowDocument = true;

        _gutter = new IssueGutterMargin(_markers);
        _editor.TextArea.LeftMargins.Insert(0, _gutter);
        _editor.TextArea.LeftMargins.Add(_playthrough);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_playthrough);
        _editor.TextArea.TextView.BackgroundRenderers.Add(_markers);
        _editor.TextArea.TextView.LineTransformers.Add(_underline);
        _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

        _editor.AddHandler(PointerPressedEvent, OnEditorPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _editor.TextArea.TextView.PointerMoved += OnTextViewPointerMoved;
        _editor.TextArea.TextView.PointerExited += (_, _) => ClearUnderline();
        _editor.PointerHover += OnPointerHover;
        _editor.PointerHoverStopped += (_, _) => ToolTip.SetIsOpen(_editor, false);
        _editor.KeyDown += OnEditorKeyDown;
        _editor.KeyUp += (_, e) =>
        {
            if (!IsNavigationModifier(e.KeyModifiers))
            {
                ClearUnderline();
            }
        };

        Content = _editor;
        ActualThemeVariantChanged += (_, _) => InstallTextMate();
    }

    /// <summary>The TextMate theme to highlight with. Defaults to Light+/Dark+ following the theme variant.</summary>
    public static readonly StyledProperty<ThemeName?> SyntaxThemeProperty =
        AvaloniaProperty.Register<InkEditor, ThemeName?>(nameof(SyntaxTheme));

    public ThemeName? SyntaxTheme
    {
        get => GetValue(SyntaxThemeProperty);
        set => SetValue(SyntaxThemeProperty, value);
    }

    /// <summary>Editor appearance settings.</summary>
    public AppSettings? Settings
    {
        get => GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    /// <summary>The underlying AvaloniaEdit control (exposed for tests).</summary>
    public TextEditor TextEditor => _editor;

    internal IssueMarkerRenderer Markers => _markers;

    internal PlaythroughMargin Playthrough => _playthrough;

    /// <summary>True while a navigation flash is showing (exposed for tests).</summary>
    internal bool IsFlashing => _flash is not null;

    public void FocusEditor() => _editor.TextArea.Focus();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
        {
            Bind(DataContext as EditorDocumentViewModel);
        }
        else if (change.Property == SettingsProperty)
        {
            ApplySettings();
        }
        else if (change.Property == SyntaxThemeProperty)
        {
            InstallTextMate();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InstallTextMate();
        if (_viewModel?.PendingNavigation is { } pending)
        {
            Dispatcher.UIThread.Post(() => NavigateTo(pending), DispatcherPriority.Loaded);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _textMate?.Dispose();
        _textMate = null;
        _textMateTheme = null;
    }

    private void Bind(EditorDocumentViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigationRequested -= OnNavigationRequested;
        }

        _viewModel = viewModel;
        _editor.Document = viewModel?.Document ?? new TextDocument();
        _markers.Attach(_editor.Document);

        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.NavigationRequested += OnNavigationRequested;
            UpdateMarkers();
            UpdatePlaythrough();
            if (viewModel.PendingNavigation is { } pending)
            {
                Dispatcher.UIThread.Post(() => NavigateTo(pending), DispatcherPriority.Loaded);
            }
        }

        // Re-apply highlighting to the new document.
        _textMate?.Dispose();
        _textMate = null;
        _textMateTheme = null;
        InstallTextMate();
    }

    private void InstallTextMate()
    {
        if (TopLevel.GetTopLevel(this) is null && _textMate is null)
        {
            return;
        }

        var theme = SyntaxTheme ?? (ActualThemeVariant == ThemeVariant.Dark ? ThemeName.DarkPlus : ThemeName.LightPlus);
        if (_textMate is not null && _textMateTheme == theme)
        {
            return;
        }

        try
        {
            if (_textMate is null)
            {
                _textMate = _editor.InstallTextMate(new InkTextMateRegistry(theme));
                _textMate.SetGrammar(InkTextMateRegistry.InkScope);
            }
            else
            {
                _textMate.SetTheme(new InkTextMateRegistry(theme).GetDefaultTheme());
            }

            _textMateTheme = theme;
            ApplyThemeColors();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or NullReferenceException)
        {
            // Highlighting is cosmetic; never let it break editing.
            System.Diagnostics.Trace.TraceWarning($"TextMate setup failed: {ex}");
            _textMate = null;
        }
    }

    private void ApplyThemeColors()
    {
        if (_textMate is null)
        {
            return;
        }

        Color? ThemeColor(string key) =>
            _textMate.TryGetThemeColor(key, out var value) ? Theming.ColorMath.ParseVsCode(value) : null;

        var background = ThemeColor("editor.background");
        var foreground = ThemeColor("editor.foreground");
        if (background is { } bg)
        {
            _editor.Background = new SolidColorBrush(bg);
        }
        else
        {
            _editor.ClearValue(BackgroundProperty);
        }

        if (foreground is { } fg)
        {
            _editor.Foreground = new SolidColorBrush(background is { } b ? Theming.ColorMath.Over(fg, b) : fg);
        }
        else
        {
            _editor.ClearValue(ForegroundProperty);
        }

        if (ThemeColor("editorLineNumber.foreground") is { } ln)
        {
            _editor.LineNumbersForeground = new SolidColorBrush(ln);
        }
        else if (foreground is { } f && background is { } b2)
        {
            _editor.LineNumbersForeground = new SolidColorBrush(Theming.ColorMath.Mix(Theming.ColorMath.Over(f, b2), b2, 0.5));
        }

        var highlight = ThemeColor("editor.lineHighlightBackground") ?? (background is { } b3
            ? Theming.ColorMath.Mix(b3, Theming.ColorMath.Luminance(b3) < 0.5 ? Colors.White : Colors.Black, 0.05)
            : null);
        if (highlight is { } hl)
        {
            _editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(hl);
            _editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(hl));
        }

        if (ThemeColor("editor.selectionBackground") is { } sel)
        {
            _editor.TextArea.SelectionBrush = new SolidColorBrush(sel.A == 255 ? Theming.ColorMath.WithAlpha(sel, 0x80) : sel);
        }

        if (ThemeColor("editorCursor.foreground") is { } cursor)
        {
            _editor.TextArea.CaretBrush = new SolidColorBrush(cursor);
        }
    }

    private void ApplySettings()
    {
        if (Settings is not { } settings)
        {
            return;
        }

        _editor.FontFamily = new FontFamily(settings.EditorFontFamily);
        _editor.FontSize = settings.EditorFontSize;
        _editor.WordWrap = settings.WordWrap;
        UpdatePlaythrough();
        _editor.HorizontalScrollBarVisibility = settings.WordWrap
            ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditorDocumentViewModel.Issues))
        {
            UpdateMarkers();
        }
        else if (e.PropertyName == nameof(EditorDocumentViewModel.Playthrough))
        {
            UpdatePlaythrough();
        }
    }

    private void UpdateMarkers()
    {
        if (_viewModel is null)
        {
            return;
        }

        _markers.SetIssues(_editor.Document, _viewModel.Issues);
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        _gutter.InvalidateVisual();
    }

    private void UpdatePlaythrough() =>
        _playthrough.Progress = _viewModel is not null && Settings?.ShowPlaythroughInEditor != false
            ? _viewModel.Playthrough
            : Inkitty.Core.StoryMap.FileProgress.None;

    private void OnNavigationRequested(object? sender, SourceRange range) =>
        Dispatcher.UIThread.Post(() => NavigateTo(range), DispatcherPriority.Loaded);

    private void NavigateTo(SourceRange range)
    {
        var document = _editor.Document;
        if (document.LineCount == 0)
        {
            return;
        }

        var lineNumber = Math.Clamp(range.Line, 1, document.LineCount);
        var line = document.GetLineByNumber(lineNumber);
        var column = range.IsWholeLine ? 1 : Math.Clamp(range.Start.Column, 1, line.Length + 1);
        var offset = line.Offset + column - 1;

        _editor.TextArea.Caret.Offset = offset;
        if (!range.IsWholeLine && range.End.Line == range.Start.Line && range.End.Column > range.Start.Column)
        {
            var length = Math.Min(range.End.Column - range.Start.Column, line.EndOffset - offset);
            _editor.Select(offset, Math.Max(0, length));
        }
        else
        {
            _editor.Select(offset, 0);
        }

        _editor.ScrollTo(lineNumber, column);
        _editor.TextArea.Caret.BringCaretToView();
        FlashLine(line);
        _viewModel?.ClearPendingNavigation();
        FocusEditor();
    }

    private void FlashLine(DocumentLine line)
    {
        // One flash at a time: a new jump replaces the previous highlight rather than stacking on it.
        ClearFlash();
        var textView = _editor.TextArea.TextView;
        _flash = new FlashRenderer(new TextSegment { StartOffset = line.Offset, Length = line.Length });
        textView.BackgroundRenderers.Add(_flash);
        textView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);

        _flashTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, (_, _) => ClearFlash());
        _flashTimer.Start();
    }

    private void ClearFlash()
    {
        _flashTimer?.Stop();
        _flashTimer = null;
        if (_flash is not null)
        {
            var textView = _editor.TextArea.TextView;
            textView.BackgroundRenderers.Remove(_flash);
            textView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
            _flash = null;
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            var location = _editor.TextArea.Caret.Location;
            _viewModel.Caret = new SourcePosition(location.Line, location.Column);
        }
    }

    private bool IsNavigationModifier(KeyModifiers modifiers)
    {
        var command = Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;
        if (modifiers.HasFlag(command))
        {
            return true;
        }

        return (Settings?.AltClickNavigates ?? !OperatingSystem.IsLinux()) && modifiers.HasFlag(KeyModifiers.Alt);
    }

    private SourcePosition? PositionAt(Point pointInEditor)
    {
        var textView = _editor.TextArea.TextView;
        var position = textView.GetPosition(pointInEditor + textView.ScrollOffset);
        if (position is not { } p || p.IsAtEndOfLine)
        {
            return null;
        }

        return new SourcePosition(p.Line, p.Column);
    }

    private void OnEditorPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || !e.GetCurrentPoint(_editor).Properties.IsLeftButtonPressed || !IsNavigationModifier(e.KeyModifiers))
        {
            return;
        }

        if (PositionAt(e.GetPosition(_editor.TextArea.TextView)) is { } position && _viewModel.GoToDefinitionAt(position))
        {
            ClearUnderline();
            e.Handled = true;
        }
    }

    private void OnTextViewPointerMoved(object? sender, PointerEventArgs e)
    {
        _lastPointer = e.GetPosition(_editor.TextArea.TextView);
        if (_viewModel is null || !IsNavigationModifier(e.KeyModifiers))
        {
            ClearUnderline();
            return;
        }

        UpdateUnderline(_lastPointer.Value);
    }

    private void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12 && e.KeyModifiers == KeyModifiers.None && _viewModel is not null)
        {
            var caret = _editor.TextArea.Caret.Location;
            _viewModel.GoToDefinitionAt(new SourcePosition(caret.Line, caret.Column));
            e.Handled = true;
            return;
        }

        if (_lastPointer is { } pointer && IsNavigationModifier(e.KeyModifiers))
        {
            UpdateUnderline(pointer);
        }
    }

    private void UpdateUnderline(Point pointInTextView)
    {
        var reference = PositionAt(pointInTextView) is { } position ? _viewModel?.FindReferenceAt(position) : null;
        if (reference?.Target is null || _editor.Document is not { } document || reference.Range.Line > document.LineCount)
        {
            ClearUnderline();
            return;
        }

        var line = document.GetLineByNumber(reference.Range.Line);
        var start = line.Offset + Math.Clamp(reference.Range.Start.Column - 1, 0, line.Length);
        var end = line.Offset + Math.Clamp(reference.Range.End.Column - 1, 0, line.Length);
        var segment = new TextSegment { StartOffset = start, Length = Math.Max(0, end - start) };
        if (_underline.Segment is { } old && old.Offset == segment.StartOffset && old.Length == segment.Length)
        {
            return;
        }

        _underline.Segment = segment;
        _editor.TextArea.TextView.Cursor = new Cursor(StandardCursorType.Hand);
        _editor.TextArea.TextView.Redraw();
    }

    private void ClearUnderline()
    {
        if (_underline.Segment is null)
        {
            return;
        }

        _underline.Segment = null;
        _editor.TextArea.TextView.Cursor = new Cursor(StandardCursorType.Ibeam);
        _editor.TextArea.TextView.Redraw();
    }

    private void OnPointerHover(object? sender, PointerEventArgs e)
    {
        var textView = _editor.TextArea.TextView;
        var position = textView.GetPosition(e.GetPosition(textView) + textView.ScrollOffset);
        if (position is not { } p)
        {
            return;
        }

        var offset = _editor.Document.GetOffset(p.Location);
        var messages = _markers.MarkersAt(offset).Where(m => m.Issue.Severity != IssueSeverity.Todo).Select(m => $"{IssueGutterMargin.Label(m.Issue.Severity)}: {m.Issue.Message}").Distinct().ToList();
        if (messages.Count == 0)
        {
            return;
        }

        ToolTip.SetTip(_editor, string.Join("\n", messages));
        ToolTip.SetIsOpen(_editor, true);
        e.Handled = true;
    }

    /// <summary>Briefly highlights a navigation target line.</summary>
    private sealed class FlashRenderer(ISegment segment) : AvaloniaEdit.Rendering.IBackgroundRenderer
    {
        private static readonly IBrush Brush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xD5, 0x4F)).ToImmutable();

        public AvaloniaEdit.Rendering.KnownLayer Layer => AvaloniaEdit.Rendering.KnownLayer.Background;

        public void Draw(AvaloniaEdit.Rendering.TextView textView, DrawingContext drawingContext)
        {
            foreach (var rect in AvaloniaEdit.Rendering.BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                drawingContext.FillRectangle(Brush, new Rect(0, rect.Top, textView.Bounds.Width, rect.Height));
            }
        }
    }
}
