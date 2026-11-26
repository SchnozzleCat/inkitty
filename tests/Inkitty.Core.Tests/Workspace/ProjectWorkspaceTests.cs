using System.Text;
using Inkitty.Core.Projects;
using Inkitty.Core.Tests.Infrastructure;
using Inkitty.Core.Workspace;

namespace Inkitty.Core.Tests.Workspace;

public class ProjectWorkspaceTests
{
    [Test]
    public async Task Creates_projects_with_unique_names_and_lists_them()
    {
        using var dir = new TempDirectory();
        var workspace = new ProjectWorkspace(dir.Path);

        var first = await workspace.CreateAsync("My story", "Hello");
        var second = await workspace.CreateAsync("My story", "Again");

        await Assert.That(Path.GetFileName(Path.GetDirectoryName(first))).IsEqualTo("My story");
        await Assert.That(Path.GetFileName(Path.GetDirectoryName(second))).IsEqualTo("My story 2");
        await Assert.That(File.ReadAllText(first)).IsEqualTo("Hello");
        await Assert.That(workspace.List().Select(p => p.Name)).IsEquivalentTo(["My story", "My story 2"]);
        await Assert.That(InkProject.Open(second).Main.Text).IsEqualTo("Again");
    }

    [Test]
    [Arguments("a/b", "a-b")]
    [Arguments("  ..  ", "story")]
    [Arguments("", "story")]
    [Arguments("x:y*z", "x-y-z")]
    public async Task Sanitises_names(string input, string expected)
    {
        await Assert.That(ProjectWorkspace.SanitizeName(input)).IsEqualTo(expected);
    }

    [Test]
    public async Task Import_picks_main_ink_then_preferred_then_first_top_level_file()
    {
        using var dir = new TempDirectory();
        var workspace = new ProjectWorkspace(dir.Path);
        static (string, byte[]) F(string path, string text = "x") => (path, Encoding.UTF8.GetBytes(text));

        var withMain = await workspace.ImportAsync("a", [F("b.ink"), F("main.ink"), F("chapters/c.ink")]);
        var preferred = await workspace.ImportAsync("b", [F("a.ink"), F("story.ink")], preferredMain: "story.ink");
        var first = await workspace.ImportAsync("c", [F("z.ink"), F("y.ink"), F("sub/a.ink")]);

        await Assert.That(Path.GetFileName(withMain)).IsEqualTo("main.ink");
        await Assert.That(File.Exists(Path.Join(Path.GetDirectoryName(withMain), "chapters", "c.ink"))).IsTrue();
        await Assert.That(Path.GetFileName(preferred)).IsEqualTo("story.ink");
        await Assert.That(Path.GetFileName(first)).IsEqualTo("y.ink");
    }

    [Test]
    public async Task Import_skips_unsafe_and_non_ink_entries_and_needs_a_top_level_file()
    {
        using var dir = new TempDirectory();
        var workspace = new ProjectWorkspace(dir.Path);
        static (string, byte[]) F(string path) => (path, [65]);

        var main = await workspace.ImportAsync("s", [F("main.ink"), F("../evil.ink"), F("/abs.ink"), F(".hidden/x.ink"), F("notes.txt")]);
        var folder = Path.GetDirectoryName(main)!;

        await Assert.That(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetFileName(f)!)).IsEquivalentTo(["main.ink"]);
        await Assert.That(File.Exists(Path.Join(dir.Path, "evil.ink"))).IsFalse();
        await Assert.That(async () => await workspace.ImportAsync("t", [F("sub/only.ink")])).Throws<InvalidDataException>();
    }

    [Test]
    public async Task Delete_removes_the_project_folder()
    {
        using var dir = new TempDirectory();
        var workspace = new ProjectWorkspace(dir.Path);
        await workspace.CreateAsync("keep", "a");
        await workspace.CreateAsync("drop", "b");

        workspace.Delete(workspace.Find("drop")!);

        await Assert.That(workspace.List().Select(p => p.Name)).IsEquivalentTo(["keep"]);
        await Assert.That(Directory.Exists(Path.Join(dir.Path, "drop"))).IsFalse();
    }

    [Test]
    public async Task Lists_folders_without_main_ink_by_their_first_ink_file_and_skips_empty_ones()
    {
        using var dir = new TempDirectory();
        dir.Write("odd/b.ink", "b");
        dir.Write("odd/a.ink", "a");
        dir.Write("empty/readme.txt", "-");

        var projects = new ProjectWorkspace(dir.Path).List();

        await Assert.That(projects.Select(p => (p.Name, Path.GetFileName(p.MainFilePath), p.FileCount))).IsEquivalentTo([("odd", "a.ink", 2)]);
    }

    [Test]
    public async Task Names_the_project_a_path_belongs_to()
    {
        using var dir = new TempDirectory();
        var workspace = new ProjectWorkspace(dir.Path);
        var main = await workspace.CreateAsync("My story", "a");

        await Assert.That(workspace.ProjectNameOf(main)).IsEqualTo("My story");
        await Assert.That(workspace.ProjectNameOf(Path.GetDirectoryName(main)!)).IsEqualTo("My story");
        await Assert.That(workspace.ProjectNameOf(Path.Join(dir.Path, "My story", "sub", "x.ink"))).IsEqualTo("My story");
        await Assert.That(workspace.ProjectNameOf(dir.Path)).IsNull();
        await Assert.That(workspace.ProjectNameOf(Path.GetTempPath())).IsNull();
    }
}
