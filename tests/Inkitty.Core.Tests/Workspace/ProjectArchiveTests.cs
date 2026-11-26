using System.IO.Compression;
using System.Text;
using Inkitty.Core.Projects;
using Inkitty.Core.Tests.Infrastructure;
using Inkitty.Core.Workspace;

namespace Inkitty.Core.Tests.Workspace;

public class ProjectArchiveTests
{
    private static Dictionary<string, string> Entries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        return archive.Entries.ToDictionary(e => e.FullName, e =>
        {
            using var reader = new StreamReader(e.Open());
            return reader.ReadToEnd();
        });
    }

    [Test]
    public async Task Exports_ink_files_with_unsaved_edits_and_new_includes()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "INCLUDE chapters/a.ink\nHello");
        dir.Write("chapters/a.ink", "A on disk");
        dir.Write("loose.ink", "not included");
        dir.Write("notes.txt", "skip me");
        dir.Write(".git/x.ink", "hidden");
        var project = InkProject.Open(main);
        project.Find("chapters/a.ink")!.SetText("A edited");
        project.Main.SetText("INCLUDE chapters/a.ink\nINCLUDE new.ink\nHello");
        project.RefreshIncludes();

        var entries = Entries(ProjectArchive.Create(project));

        await Assert.That(entries.Keys).IsEquivalentTo(["chapters/a.ink", "loose.ink", "main.ink", "new.ink"]);
        await Assert.That(entries["chapters/a.ink"]).IsEqualTo("A edited");
        await Assert.That(entries["new.ink"]).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task Reads_ink_files_from_a_zip_stripping_a_single_top_folder()
    {
        var zip = Zip(("story/main.ink", "M"), ("story/ch/a.ink", "A"), ("story/readme.md", "-"), ("story/../../evil.ink", "E"));

        var files = ProjectArchive.ReadInkFiles(new MemoryStream(zip));

        await Assert.That(files.Select(f => (f.Path, Encoding.UTF8.GetString(f.Contents)))).IsEquivalentTo([("main.ink", "M"), ("ch/a.ink", "A")]);
    }

    [Test]
    public async Task Keeps_paths_when_files_are_at_the_zip_root()
    {
        var zip = Zip(("main.ink", "M"), ("ch/a.ink", "A"));

        var files = ProjectArchive.ReadInkFiles(new MemoryStream(zip));

        await Assert.That(files.Select(f => f.Path)).IsEquivalentTo(["main.ink", "ch/a.ink"]);
    }

    [Test]
    public async Task Round_trips_through_a_workspace_import()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("src/main.ink", "INCLUDE b.ink\nHi");
        dir.Write("src/b.ink", "B");
        var zip = ProjectArchive.Create(InkProject.Open(main));

        var workspace = new ProjectWorkspace(Path.Join(dir.Path, "ws"));
        var imported = InkProject.Open(await workspace.ImportAsync("copy", ProjectArchive.ReadInkFiles(new MemoryStream(zip))));

        await Assert.That(imported.Files.Select(f => (f.RelativePath, f.Text))).IsEquivalentTo([("main.ink", "INCLUDE b.ink\nHi"), ("b.ink", "B")]);
    }

    private static byte[] Zip(params (string Name, string Text)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }
}
