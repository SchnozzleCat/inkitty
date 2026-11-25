using Inkitty.App.Services;

namespace Inkitty.App.Tests.Infrastructure;

/// <summary>Scriptable dialogs for UI tests.</summary>
public sealed class FakeDialogService : IDialogService
{
    public string? NextOpenPath { get; set; }

    public string? NextNewProjectPath { get; set; }

    public UnsavedChangesChoice UnsavedChoice { get; set; } = UnsavedChangesChoice.Discard;

    public List<string> Errors { get; } = [];

    public int UnsavedPrompts { get; private set; }

    public Task<string?> PickInkFileToOpenAsync() => Task.FromResult(NextOpenPath);

    public Task<string?> PickNewProjectFileAsync() => Task.FromResult(NextNewProjectPath);

    public Task<UnsavedChangesChoice> AskUnsavedChangesAsync(string what)
    {
        UnsavedPrompts++;
        return Task.FromResult(UnsavedChoice);
    }

    public Task ShowErrorAsync(string title, string message)
    {
        Errors.Add($"{title}: {message}");
        return Task.CompletedTask;
    }
}
