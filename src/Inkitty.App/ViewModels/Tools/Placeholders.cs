namespace Inkitty.App.ViewModels.Tools;

/// <summary>Empty-state messages shared by the tool panes, so they all read the same.</summary>
public static class Placeholders
{
    /// <summary>Shown by every project-wide pane when nothing is open.</summary>
    public const string NoProject = "No project open";

    /// <summary>Shown by per-file panes (the outline) when no file is active.</summary>
    public const string NoFile = "No file open";
}
