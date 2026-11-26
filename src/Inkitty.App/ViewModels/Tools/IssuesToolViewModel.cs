using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Messages;
using Inkitty.Core.Diagnostics;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>A row in the issue list.</summary>
public sealed record IssueItem(Issue Issue)
{
    public IssueSeverity Severity => Issue.Severity;

    public string Message => Issue.Message;

    public string File => Issue.File ?? string.Empty;

    public string Line => Issue.Line?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    public string Location => Issue.Range is { } r ? $"{r.File}:{r.Line}" : string.Empty;

    public string SeverityLabel => Issue.Severity switch
    {
        IssueSeverity.Error => "Error",
        IssueSeverity.Warning => "Warning",
        IssueSeverity.Todo => "TODO",
        IssueSeverity.RuntimeError => "Runtime error",
        IssueSeverity.RuntimeWarning => "Runtime warning",
        _ => Issue.Severity.ToString(),
    };

    /// <summary>Style class used to pick the icon/colour.</summary>
    public string SeverityClass => Issue.Severity switch
    {
        IssueSeverity.Error or IssueSeverity.RuntimeError => "error",
        IssueSeverity.Warning or IssueSeverity.RuntimeWarning => "warning",
        _ => "todo",
    };
}

/// <summary>The issue browser: errors, warnings and TODOs across the project.</summary>
public sealed partial class IssuesToolViewModel : Tool
{
    private readonly IMessenger _messenger;
    private IReadOnlyList<Issue> _all = [];
    private int _cursor = -1;

    public IssuesToolViewModel(IMessenger messenger)
    {
        _messenger = messenger;
        Id = DockIds.Issues;
        Title = "Issues";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
    }

    public ObservableCollection<IssueItem> Items { get; } = [];

    [ObservableProperty]
    public partial IssueItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial bool ShowErrors { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowWarnings { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowTodos { get; set; } = true;

    [ObservableProperty]
    public partial int ErrorCount { get; private set; }

    [ObservableProperty]
    public partial int WarningCount { get; private set; }

    [ObservableProperty]
    public partial int TodoCount { get; private set; }

    public void SetIssues(IReadOnlyList<Issue> issues)
    {
        _all = issues;
        ErrorCount = issues.Count(i => i.Severity.IsError);
        WarningCount = issues.Count(i => i.Severity.IsWarning);
        TodoCount = issues.Count(i => i.Severity == IssueSeverity.Todo);
        Refilter();
    }

    /// <summary>Opens the selected issue's location.</summary>
    [RelayCommand]
    private void Open(IssueItem? item)
    {
        item ??= SelectedItem;
        if (item?.Issue.Range is { } range)
        {
            _cursor = Items.IndexOf(item);
            _messenger.Send(new NavigateToSourceMessage(range.File, range));
        }
    }

    /// <summary>Goes to the next issue with a location (F8), cycling.</summary>
    [RelayCommand]
    private void Next() => Step(+1);

    /// <summary>Goes to the previous issue with a location (Shift+F8), cycling.</summary>
    [RelayCommand]
    private void Previous() => Step(-1);

    partial void OnShowErrorsChanged(bool value) => Refilter();

    partial void OnShowWarningsChanged(bool value) => Refilter();

    partial void OnShowTodosChanged(bool value) => Refilter();

    private void Step(int direction)
    {
        var navigable = Items.Where(i => i.Issue.Range is not null).ToList();
        if (navigable.Count == 0)
        {
            _messenger.Send(new StatusMessage("No issues"));
            return;
        }

        var current = _cursor >= 0 && _cursor < Items.Count ? navigable.IndexOf(Items[_cursor]) : -1;
        var next = current < 0
            ? (direction > 0 ? 0 : navigable.Count - 1)
            : (current + direction + navigable.Count) % navigable.Count;

        SelectedItem = navigable[next];
        Open(navigable[next]);
        _messenger.Send(new StatusMessage($"{navigable[next].SeverityLabel}: {navigable[next].Message}"));
    }

    private void Refilter()
    {
        var selected = SelectedItem?.Issue;
        Items.Clear();
        foreach (var issue in _all.Where(Include))
        {
            Items.Add(new IssueItem(issue));
        }

        SelectedItem = selected is null ? null : Items.FirstOrDefault(i => i.Issue == selected);
        _cursor = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
    }

    private bool Include(Issue issue) =>
        issue.Severity.IsError ? ShowErrors
        : issue.Severity.IsWarning ? ShowWarnings
        : ShowTodos;
}
