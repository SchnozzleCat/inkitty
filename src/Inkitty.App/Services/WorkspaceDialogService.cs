using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Inkitty.Core.Workspace;

namespace Inkitty.App.Services;

/// <summary>
/// Dialogs for hosts without a user-visible file system (the browser): projects live in a
/// <see cref="ProjectWorkspace"/>; files come in by import and leave by export (download).
/// </summary>
public sealed class WorkspaceDialogService(ProjectWorkspace workspace, Func<string, byte[], Task>? download = null) : DialogService
{
    private const string NewProjectTemplate = """
        // Welcome to Inkitty! Start writing your story here.
        Once upon a time...

        * [There were two choices.] They lived happily ever after.
            -> END
        * [There were four lines of content.]
            -> END
        """;

    public ProjectWorkspace Workspace => workspace;

    /// <summary>Replaces the modal host (tests).</summary>
    public IModalHost? ModalHostOverride { get; set; }

    protected override IModalHost? ModalHost => ModalHostOverride ?? base.ModalHost;

    /// <summary>Shows the workspace's projects; open one, delete one, or import files as a new one.</summary>
    public override async Task<string?> PickInkFileToOpenAsync()
    {
        if (ModalHost is not { } host)
        {
            return null;
        }

        string? result = null;
        var list = new StackPanel { Spacing = 2 };
        var empty = new TextBlock
        {
            Text = "No projects yet. Create one with File > New Project, or import .ink files or a .zip.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7,
        };

        void Refresh()
        {
            list.Children.Clear();
            var projects = workspace.List();
            empty.IsVisible = projects.Count == 0;
            foreach (var project in projects)
            {
                list.Children.Add(ProjectRow(project, () =>
                {
                    result = project.MainFilePath;
                    host.CloseModal();
                }, async () =>
                {
                    if (await ConfirmDeleteAsync(project))
                    {
                        workspace.Delete(project);
                    }

                    Refresh();
                }));
            }
        }

        Refresh();
        var buttons = ButtonRow(
        [
            ("Import folder…", false, async () =>
            {
                if (await ImportFolderAsync() is { } imported)
                {
                    result = imported;
                    host.CloseModal();
                }
            }),
            ("Import files…", false, async () =>
            {
                if (await ImportAsync() is { } imported)
                {
                    result = imported;
                    host.CloseModal();
                }
            }),
            ("Cancel", false, host.CloseModal),
        ]);

        var content = new DockPanel { Margin = new Thickness(20), Width = 480 };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        buttons.Margin = new Thickness(0, 16, 0, 0);
        content.Children.Add(buttons);
        var note = new TextBlock
        {
            Text = "Projects are stored in this browser. Use File > Export Project as .zip to keep a copy.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.6,
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(note, Avalonia.Controls.Dock.Bottom);
        content.Children.Add(note);
        DockPanel.SetDock(empty, Avalonia.Controls.Dock.Top);
        content.Children.Add(empty);
        content.Children.Add(new ScrollViewer { Content = list, MaxHeight = 360 });

        await host.ShowModalAsync("Open project", content);
        return result;
    }

    /// <summary>Asks for a project name and creates it with a template main file.</summary>
    public override async Task<string?> PickNewProjectFileAsync()
    {
        if (await PromptAsync("New project", "Project name", workspace.UniqueName("story")) is not { } name)
        {
            return null;
        }

        try
        {
            return await workspace.CreateAsync(name, NewProjectTemplate);
        }
        catch (IOException ex)
        {
            await ShowErrorAsync("Couldn't create project", ex.Message);
            return null;
        }
    }

    public override async Task<bool> SaveCopyAsync(string suggestedFileName, byte[] contents)
    {
        if (download is null)
        {
            return await base.SaveCopyAsync(suggestedFileName, contents);
        }

        await download(suggestedFileName, contents);
        return true;
    }

    /// <summary>Imports picked .ink files or a .zip as a new workspace project; returns its main file.</summary>
    public async Task<string?> ImportAsync()
    {
        if (StorageProvider is not { CanOpen: true } storage)
        {
            return null;
        }

        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import .ink files or a .zip",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("ink files or zip") { Patterns = ["*.ink", "*.zip"] }, FilePickerFileTypes.All],
        });
        if (picked.Count == 0)
        {
            return null;
        }

        try
        {
            List<(string Path, byte[] Contents)> files = [];
            string? name = null;
            foreach (var item in picked)
            {
                await using var stream = await item.OpenReadAsync();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                buffer.Position = 0;
                if (item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    files.AddRange(ProjectArchive.ReadInkFiles(buffer));
                    name ??= Path.GetFileNameWithoutExtension(item.Name);
                }
                else
                {
                    files.Add((item.Name, buffer.ToArray()));
                }
            }

            var firstInk = picked.FirstOrDefault(p => p.Name.EndsWith(".ink", StringComparison.OrdinalIgnoreCase))?.Name;
            name ??= firstInk is { } ink && !ink.Equals(ProjectWorkspace.MainFileName, StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(ink)
                : "imported story";
            var main = await workspace.ImportAsync(name, files, preferredMain: firstInk);
            await WarnAboutMissingIncludesAsync(main);
            return main;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Import failed", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Imports a picked folder's ink files (subfolders included) as a new workspace project; returns
    /// its main file. Picking single files can't bring in what they INCLUDE: the browser only hands
    /// over the files picked.
    /// </summary>
    public async Task<string?> ImportFolderAsync()
    {
        if (StorageProvider is not { CanPickFolder: true } storage)
        {
            await ShowErrorAsync("Import folder", "This browser can't open folders. Select all of the project's .ink files with Import files… instead, or import a .zip.");
            return null;
        }

        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Import a project folder" });
        if (picked.Count == 0)
        {
            return null;
        }

        try
        {
            List<(string Path, byte[] Contents)> files = [];
            await ReadInkFilesAsync(picked[0], "", files, depth: 0);
            if (files.Count == 0)
            {
                await ShowErrorAsync("Import failed", $"There are no .ink files in '{picked[0].Name}'.");
                return null;
            }

            var main = await workspace.ImportAsync(picked[0].Name, files);
            await WarnAboutMissingIncludesAsync(main);
            return main;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await ShowErrorAsync("Import failed", ex.Message);
            return null;
        }
    }

    /// <summary>Folders deeper than this aren't read (guards against huge or cyclic trees).</summary>
    private const int MaxFolderDepth = 8;

    private static async Task ReadInkFilesAsync(IStorageFolder folder, string prefix, List<(string Path, byte[] Contents)> files, int depth)
    {
        await foreach (var item in folder.GetItemsAsync())
        {
            if (item.Name.StartsWith('.'))
            {
                continue;
            }

            if (item is IStorageFolder child && depth < MaxFolderDepth)
            {
                await ReadInkFilesAsync(child, $"{prefix}{item.Name}/", files, depth + 1);
            }
            else if (item is IStorageFile file && file.Name.EndsWith(".ink", StringComparison.OrdinalIgnoreCase))
            {
                await using var stream = await file.OpenReadAsync();
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                files.Add((prefix + file.Name, buffer.ToArray()));
            }
        }
    }

    /// <summary>Says which included files weren't imported (e.g. only main.ink was picked).</summary>
    private async Task WarnAboutMissingIncludesAsync(string mainFilePath)
    {
        var missing = ProjectWorkspace.MissingIncludes(mainFilePath);
        if (missing.Count == 0)
        {
            return;
        }

        await ShowErrorAsync(
            "Some included files weren't imported",
            $"The story includes {string.Join(", ", missing)}, which {(missing.Count == 1 ? "wasn't" : "weren't")} among the imported files. "
            + "To bring them in, import the project's folder with Import folder…, or select all of its .ink files with Import files…; "
            + "or save to create them empty.");
    }

    private async Task<bool> ConfirmDeleteAsync(WorkspaceProject project) =>
        await ShowButtonsAsync(
            "Delete project",
            $"Delete '{project.Name}' and its {project.FileCount} file(s) from this browser? This can't be undone.",
            [("Delete", true, false), ("Cancel", false, true)]);

    /// <summary>Asks for a line of text; returns null if cancelled or empty.</summary>
    private async Task<string?> PromptAsync(string title, string label, string initial)
    {
        if (ModalHost is not { } host)
        {
            return null;
        }

        string? result = null;
        var box = new TextBox { Text = initial, SelectionStart = 0, SelectionEnd = initial.Length };
        void Accept()
        {
            if (!string.IsNullOrWhiteSpace(box.Text))
            {
                result = box.Text.Trim();
                host.CloseModal();
            }
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                e.Handled = true;
                Accept();
            }
        };

        var content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 10,
            Width = 360,
            Children =
            {
                new TextBlock { Text = label },
                box,
                ButtonRow([("Create", true, Accept), ("Cancel", false, host.CloseModal)]),
            },
        };

        await host.ShowModalAsync(title, content);
        return result;
    }

    private static Control ProjectRow(WorkspaceProject project, Action open, Func<Task> delete)
    {
        var openButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Classes = { "project-row" },
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = project.Name, FontWeight = FontWeight.SemiBold },
                    new TextBlock
                    {
                        Text = string.Create(CultureInfo.CurrentCulture, $"{Path.GetFileName(project.MainFilePath)} · {project.FileCount} file(s) · {project.LastModified.ToLocalTime():g}"),
                        FontSize = 12,
                        Opacity = 0.7,
                    },
                },
            },
        };
        openButton.Click += (_, _) => open();

        var deleteButton = new Button { Content = "Delete", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(deleteButton, $"Delete {project.Name}");
        deleteButton.Click += async (_, _) => await delete();

        var row = new DockPanel();
        DockPanel.SetDock(deleteButton, Avalonia.Controls.Dock.Right);
        row.Children.Add(deleteButton);
        row.Children.Add(openButton);
        return row;
    }
}
