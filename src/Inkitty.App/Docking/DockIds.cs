namespace Inkitty.App.Docking;

/// <summary>Stable ids for dockables; persisted in layout.json, so don't rename them.</summary>
public static class DockIds
{
    public const string Root = "Root";
    public const string MainLayout = "MainLayout";
    public const string LeftDock = "LeftDock";
    public const string CenterDock = "CenterDock";
    public const string RightDock = "RightDock";
    public const string BottomDock = "BottomDock";
    public const string Documents = "Documents";
    public const string Files = "Files";
    public const string Player = "Player";
    public const string Issues = "Issues";

    public const string DocumentPrefix = "doc:";
}
