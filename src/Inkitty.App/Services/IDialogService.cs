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
    /// <summary>Asks for a main ink file to open; returns its path (a local or workspace path).</summary>
    Task<string?> PickInkFileToOpenAsync();

    /// <summary>Asks where to create a new project; returns the main file path.</summary>
    Task<string?> PickNewProjectFileAsync();

    Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string what);

    Task ShowErrorAsync(string title, string message);

    /// <summary>
    /// Saves a copy of some content outside the project (a save picker on desktop, a download in
    /// the browser). Returns false if the user cancelled.
    /// </summary>
    Task<bool> SaveCopyAsync(string suggestedFileName, byte[] contents);
}
