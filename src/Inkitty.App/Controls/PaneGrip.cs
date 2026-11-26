using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;

namespace Inkitty.App.Controls;

/// <summary>
/// A tool pane's header bar, replacing Dock's title bar: shows the pane's title and a close button,
/// drag it to move the pane, right-click it to float or close it. Styled in Styles/Inkitty.axaml.
/// </summary>
[TemplatePart("PART_CloseButton", typeof(Button))]
[PseudoClasses(":floating")]
public sealed class PaneGrip : TemplatedControl
{
    public static readonly StyledProperty<IDockable?> DockableProperty =
        AvaloniaProperty.Register<PaneGrip, IDockable?>(nameof(Dockable));

    private Button? _closeButton;

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
        }
    }

    /// <summary>
    /// In a floating pane the floating window's own title bar (styled to match, see Styles/Inkitty.axaml)
    /// is the header, so this one hides there (<c>:floating</c>).
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        PseudoClasses.Set(":floating", this.FindAncestorOfType<MdiDocumentWindow>() is not null);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _closeButton?.Click -= OnCloseClicked;
        _closeButton = e.NameScope.Find<Button>("PART_CloseButton");
        _closeButton?.Click += OnCloseClicked;
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
        var floatItem = new MenuItem { Header = "Float" };
        floatItem.Click += (_, _) =>
        {
            if (Dockable is { Factory: { } factory } dockable && dockable.CanFloat)
            {
                factory.FloatDockable(dockable);
            }
        };

        var closeItem = new MenuItem { Header = "Close" };
        closeItem.Click += (_, _) => Close();

        return new ContextMenu { ItemsSource = new[] { floatItem, closeItem } };
    }
}
