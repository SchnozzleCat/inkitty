using System.Runtime.InteropServices.JavaScript;
using Avalonia.Browser;
using Dock.Model.Core;
using Dock.Settings;
using Inkitty.App;
using Inkitty.App.Services;
using Inkitty.Core.Workspace;

namespace Inkitty.Browser;

internal static partial class Program
{
    /// <summary>Persistent root (IndexedDB, mounted by main.js before .NET starts).</summary>
    private const string Root = "/inkitty";

    private static readonly string ProjectsDirectory = Path.Join(Root, "projects");
    private static readonly string ConfigDirectory = Path.Join(Root, "config");

    private static async Task Main(string[] args)
    {
        await JSHost.ImportAsync("inkitty", "../inkitty.js");
        Directory.CreateDirectory(ProjectsDirectory);
        Directory.CreateDirectory(ConfigDirectory);

        var workspace = new ProjectWorkspace(ProjectsDirectory);
        await SeedSampleAsync(workspace);
        Console.WriteLine($"Inkitty: {workspace.List().Count} project(s) in browser storage");

        App.App.Options = new AppOptions
        {
            ConfigDirectory = ConfigDirectory,
            WatchFiles = false, // nothing else writes the workspace
            Dialogs = new WorkspaceDialogService(workspace, DownloadAsync),
            ProjectNamer = project => workspace.ProjectNameOf(project.RootDirectory),
            StartupProject = shell => OpenStartupProjectAsync(shell, workspace),
            ShellCreated = (shell, _) => HookPage(shell),
        };

        await InkittyAppBuilder.Configure()
            .UseFloatingWindowHostMode(DockFloatingWindowHostMode.Managed)
            .StartBrowserAppAsync("out");
    }

    /// <summary>Reopens the last project, or the sample on first visit.</summary>
    private static async Task OpenStartupProjectAsync(App.ViewModels.MainWindowViewModel shell, ProjectWorkspace workspace)
    {
        var projects = workspace.List();
        var recent = shell.RecentProjects.FirstOrDefault(p => workspace.IsInside(p) && File.Exists(p))
            ?? (projects.Count > 0 ? projects[0].MainFilePath : null);
        if (recent is not null)
        {
            Console.WriteLine($"Inkitty: opening {recent}");
            await shell.OpenProjectAsync(recent);
        }
    }

    /// <summary>On first visit, put the bundled sample in the workspace so there's something to play.</summary>
    private static async Task SeedSampleAsync(ProjectWorkspace workspace)
    {
        var marker = Path.Join(ConfigDirectory, "seeded");
        if (File.Exists(marker))
        {
            return;
        }

        var assembly = typeof(Program).Assembly;
        const string prefix = "Inkitty.Browser.Sample.";
        List<(string Path, byte[] Contents)> files = [];
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);

            // LogicalName is "Inkitty.Browser.Sample.<relative path>" (see the csproj).
            files.Add((name[prefix.Length..].Replace('\\', '/'), buffer.ToArray()));
        }

        if (files.Count > 0)
        {
            await workspace.ImportAsync("The Intercept", files);
        }

        await File.WriteAllTextAsync(marker, DateTimeOffset.UtcNow.ToString("O"));
        Interop.Persist();
    }

    private static void HookPage(App.ViewModels.MainWindowViewModel shell)
    {
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(shell.WindowTitle))
            {
                Interop.SetTitle(shell.Project is { } p ? $"Inkitty — {p.Name}" : "Inkitty");
            }
        };

        // The page asks (synchronously) before unloading whether there are unsaved edits.
        Interop.SetUnsavedChangesCheck(() => shell.Project?.Project.DirtyFiles.Count > 0);
    }

    private static Task DownloadAsync(string fileName, byte[] contents)
    {
        Interop.Download(fileName, contents, fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "text/plain");
        return Task.CompletedTask;
    }
}

/// <summary>Calls into wwwroot/inkitty.js.</summary>
internal static partial class Interop
{
    [JSImport("setTitle", "inkitty")]
    public static partial void SetTitle(string title);

    [JSImport("setUnsavedChangesCheck", "inkitty")]
    public static partial void SetUnsavedChangesCheck([JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> check);

    [JSImport("download", "inkitty")]
    public static partial void Download(string fileName, byte[] contents, string mimeType);

    [JSImport("persist", "inkitty")]
    public static partial void Persist();
}
