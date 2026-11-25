namespace Inkitty.App.Services;

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>File pickers and modal prompts.</summary>
public interface IDialogService
{
    Task<string?> PickInkFileToOpenAsync();

    Task<string?> PickNewProjectFileAsync();

    Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string what);

    Task ShowErrorAsync(string title, string message);
}
