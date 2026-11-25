using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Inkitty.App.Services;

/// <summary>Dialogs implemented with Avalonia's StorageProvider and a small modal window.</summary>
public sealed class DialogService : IDialogService
{
    private static readonly FilePickerFileType InkFiles = new("ink files") { Patterns = ["*.ink"], AppleUniformTypeIdentifiers = ["public.plain-text"] };

    private static Window? Owner =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public async Task<string?> PickInkFileToOpenAsync()
    {
        if (Owner is not { } owner)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open main ink file",
            AllowMultiple = false,
            FileTypeFilter = [InkFiles, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickNewProjectFileAsync()
    {
        if (Owner is not { } owner)
        {
            return null;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create new ink project",
            SuggestedFileName = "main.ink",
            DefaultExtension = "ink",
            FileTypeChoices = [InkFiles],
        });
        return file?.TryGetLocalPath();
    }

    public async Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string what)
    {
        var result = await ShowButtonsAsync<UnsavedChangesChoice?>(
            "Unsaved changes",
            $"{what} has unsaved changes. Save them?",
            [("Save", UnsavedChangesChoice.Save, true), ("Don't save", UnsavedChangesChoice.Discard, false), ("Cancel", UnsavedChangesChoice.Cancel, false)]);
        return result ?? UnsavedChangesChoice.Cancel;
    }

    public Task ShowErrorAsync(string title, string message) =>
        ShowButtonsAsync(title, message, [("OK", true, true)]);

    private static async Task<T?> ShowButtonsAsync<T>(string title, string message, IReadOnlyList<(string Label, T Value, bool IsDefault)> buttons)
    {
        if (Owner is not { } owner)
        {
            return default;
        }

        T? result = default;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            MinWidth = 360,
        };

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, value, isDefault) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) =>
            {
                result = value;
                dialog.Close();
            };
            buttonRow.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 480 },
                buttonRow,
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
