using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Inkitty.App.Input;

/// <summary>
/// <c>input:Shortcut.Id="file.save"</c> on a <see cref="MenuItem"/> or <see cref="NativeMenuItem"/>
/// labels it with that command's current shortcut (see <see cref="ShortcutCatalog"/>).
/// </summary>
public sealed class Shortcut : AvaloniaObject
{
    public static readonly AttachedProperty<string?> IdProperty =
        AvaloniaProperty.RegisterAttached<Shortcut, AvaloniaObject, string?>("Id");

    private Shortcut()
    {
    }

    public static string? GetId(AvaloniaObject element) => element.GetValue(IdProperty);

    public static void SetId(AvaloniaObject element, string? value) => element.SetValue(IdProperty, value);

    /// <summary>Sets the shortcut text of every labelled item in an in-window menu.</summary>
    public static void Label(Menu menu, ShortcutService shortcuts)
    {
        foreach (var item in menu.GetLogicalDescendants().OfType<MenuItem>())
        {
            if (GetId(item) is { } id)
            {
                item.InputGesture = shortcuts.GestureFor(id);
            }
        }
    }

    /// <summary>
    /// Sets the shortcuts of every labelled item in a native (macOS) menu. These are live key
    /// equivalents, so they're removed while the shortcuts are suspended.
    /// </summary>
    public static void Label(NativeMenu menu, ShortcutService shortcuts)
    {
        foreach (var item in menu.Items.OfType<NativeMenuItem>())
        {
            if (GetId(item) is { } id)
            {
                item.Gesture = shortcuts.IsSuspended ? null : shortcuts.GestureFor(id);
            }

            if (item.Menu is { } submenu)
            {
                Label(submenu, shortcuts);
            }
        }
    }

    /// <summary>Replaces <paramref name="bindings"/> with a binding for every command that has a shortcut.</summary>
    public static void Bind(IList<KeyBinding> bindings, ShortcutService shortcuts, ViewModels.MainWindowViewModel vm)
    {
        bindings.Clear();
        if (shortcuts.IsSuspended)
        {
            return;
        }

        foreach (var command in ShortcutCatalog.Commands)
        {
            if (shortcuts.GestureFor(command.Id) is { } gesture)
            {
                bindings.Add(new KeyBinding { Gesture = gesture, Command = command.Command(vm) });
            }
        }
    }
}
