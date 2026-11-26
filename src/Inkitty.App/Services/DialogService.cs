using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Inkitty.App.Views;

namespace Inkitty.App.Services;

/// <summary>Dialogs implemented with Avalonia's StorageProvider and the main view's modal host.</summary>
public class DialogService : IDialogService
{
    protected static readonly FilePickerFileType InkFiles = new("ink files") { Patterns = ["*.ink"], AppleUniformTypeIdentifiers = ["public.plain-text"] };

    /// <summary>Where modals are shown. Defaults to the attached main view.</summary>
    protected virtual IModalHost? ModalHost => MainView.Current;

    protected static IStorageProvider? StorageProvider =>
        MainView.Current is { } view ? TopLevel.GetTopLevel(view)?.StorageProvider : null;

    public virtual async Task<string?> PickInkFileToOpenAsync()
    {
        if (StorageProvider is not { CanOpen: true } storage)
        {
            return null;
        }

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open main ink file",
            AllowMultiple = false,
            FileTypeFilter = [InkFiles, FilePickerFileTypes.All],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public virtual async Task<string?> PickNewProjectFileAsync()
    {
        if (StorageProvider is not { CanSave: true } storage)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create new ink project",
            SuggestedFileName = "main.ink",
            DefaultExtension = "ink",
            FileTypeChoices = [InkFiles],
        });
        return file?.TryGetLocalPath();
    }

    public virtual async Task<bool> SaveCopyAsync(string suggestedFileName, byte[] contents)
    {
        if (StorageProvider is not { CanSave: true } storage)
        {
            return false;
        }

        var extension = Path.GetExtension(suggestedFileName).TrimStart('.');
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save a copy",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType($"{extension} files") { Patterns = [$"*.{extension}"] }],
        });
        if (file is null)
        {
            return false;
        }

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(contents);
        return true;
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

    /// <summary>Shows a message with a row of buttons; returns the clicked button's value (default if dismissed).</summary>
    protected async Task<T?> ShowButtonsAsync<T>(string title, string message, IReadOnlyList<(string Label, T Value, bool IsDefault)> buttons)
    {
        if (ModalHost is not { } host)
        {
            return default;
        }

        T? result = default;
        var buttonRow = ButtonRow(buttons.Select(b => (b.Label, b.IsDefault, (Action)(() =>
        {
            result = b.Value;
            host.CloseModal();
        }))));

        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            MinWidth = 360,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 480 },
                buttonRow,
            },
        };

        await host.ShowModalAsync(title, content);
        return result;
    }

    /// <summary>A right-aligned row of dialog buttons.</summary>
    protected static StackPanel ButtonRow(IEnumerable<(string Label, bool IsDefault, Action Click)> buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, isDefault, click) in buttons)
        {
            var button = new Button { Content = label, IsDefault = isDefault, MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            button.Click += (_, _) => click();
            row.Children.Add(button);
        }

        return row;
    }
}
