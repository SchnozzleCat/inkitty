using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Dock.Model.Core;
using Inkitty.App.Docking;

namespace Inkitty.App.Controls;

/// <summary>
/// A tool pane's header bar, replacing Dock's title bar: shows the pane's title, a pin button (auto hide:
/// the pane collapses to a tab on the window edge and slides out when that's clicked; on a slid-out pane
/// the same button unpins it) and a close button. Drag it to move the pane, right-click it for the same
/// actions plus float. Styled in Styles/Inkitty.axaml.
/// </summary>
[TemplatePart("PART_CloseButton", typeof(Button))]
[TemplatePart("PART_PinButton", typeof(Button))]
[PseudoClasses(":floating", ":pinned", ":can-pin")]
public sealed class PaneGrip : TemplatedControl
{
    public static readonly StyledProperty<IDockable?> DockableProperty =
        AvaloniaProperty.Register<PaneGrip, IDockable?>(nameof(Dockable));

    private Button? _closeButton;
    private Button? _pinButton;
    private readonly MenuItem _floatItem = new() { Header = "Float" };
    private readonly MenuItem _pinItem = new();

    // Dock starts a pane drag from a rendered control marked as a drag area whose DataContext is the
    // dockable; the template's root panel is that control.
    public PaneGrip() => ContextMenu = BuildMenu();

    /// <summary>The tool this header belongs to.</summary>
    public IDockable? Dockable
    {
        get => GetValue(DockableProperty);
        set => SetValue(DockableProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DockableProperty)
        {
            DataContext = Dockable;
            UpdatePinState();
        }
    }

    /// <summary>
    /// Pinned state and whether pinning is offered, refreshed whenever the layout changes: the same
    /// pane moves between docks, floating windows and the edge.
    /// </summary>
    private void UpdatePinState()
    {
        var factory = Dockable?.Factory as InkittyDockFactory;
        var pinned = Dockable is { } d && factory?.IsDockablePinned(d) == true;
        var canPin = factory?.CanPinTool(Dockable) == true;
        PseudoClasses.Set(":pinned", pinned);
        PseudoClasses.Set(":can-pin", canPin);
        PseudoClasses.Set(":floating", Dockable is { } tool && factory?.IsInSinglePaneFloatingWindow(tool) == true);
        _pinItem.Header = pinned ? "Unpin" : "Auto Hide";
        _pinItem.IsVisible = canPin;
        _floatItem.IsVisible = Dockable is { } t && factory?.IsInMainWindow(t) == true && !pinned;
        if (_pinButton is not null)
        {
            ToolTip.SetTip(_pinButton, pinned ? "Unpin (keep the pane open)" : "Auto hide (collapse to the window edge)");
        }
    }

    private InkittyDockFactory? _subscribed;

    private void Subscribe(InkittyDockFactory? factory)
    {
        if (ReferenceEquals(_subscribed, factory))
        {
            return;
        }

        if (_subscribed is not null)
        {
            _subscribed.LayoutChanged -= OnLayoutChanged;
        }

        _subscribed = factory;
        if (factory is not null)
        {
            factory.LayoutChanged += OnLayoutChanged;
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => UpdatePinState();

    /// <summary>
    /// When a floating window holds just this pane, the window's own title bar (styled to match, see
    /// Styles/Inkitty.axaml) is the header, so this one hides there (<c>:floating</c>).
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Dockable?.Factory as InkittyDockFactory);
        UpdatePinState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _closeButton?.Click -= OnCloseClicked;
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        _closeButton?.Click += OnCloseClicked;
        _pinButton?.Click -= OnPinClicked;
        _pinButton = e.NameScope.Find<Button>("PART_PinButton");
        _pinButton?.Click += OnPinClicked;
        UpdatePinState();
    }

    private void OnPinClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        TogglePin();
    }

    /// <summary>Auto hides the pane, or unpins it back into its dock.</summary>
    private void TogglePin()
    {
        if (Dockable is { Factory: InkittyDockFactory factory } dockable && factory.CanPinTool(dockable))
        {
            factory.PinDockable(dockable);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void Close()
    {
        if (Dockable is { Factory: { } factory } dockable && dockable.CanClose)
        {
            factory.CloseDockable(dockable);
        }
    }

    private ContextMenu BuildMenu()
    {
        _floatItem.Click += (_, _) =>
        {
            if (Dockable is { Factory: { } factory } dockable && dockable.CanFloat)
            {
                factory.FloatDockable(dockable);
            }
        };

        _pinItem.Click += (_, _) => TogglePin();

        var closeItem = new MenuItem { Header = "Close" };
        closeItem.Click += (_, _) => Close();

        var menu = new ContextMenu { ItemsSource = new[] { _floatItem, _pinItem, closeItem } };
        menu.Opening += (_, _) => UpdatePinState();
        return menu;
    }
}
