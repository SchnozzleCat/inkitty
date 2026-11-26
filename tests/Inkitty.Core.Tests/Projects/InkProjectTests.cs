using Inkitty.Core.FileSystem;
using Inkitty.Core.Projects;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.Projects;

public class InkProjectTests
{
    [Test]
    public async Task Discovers_include_graph_in_depth_first_order()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "INCLUDE a.ink\nINCLUDE sub/b.ink\nHello");
        dir.Write("a.ink", "INCLUDE sub/c.ink\n");
        dir.Write("sub/b.ink", "B");
        dir.Write("sub/c.ink", "C");

        var project = InkProject.Open(main);

        await Assert.That(project.Files.Select(f => f.RelativePath)).IsEquivalentTo(
            ["main.ink", "a.ink", "sub/c.ink", "sub/b.ink"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task Tolerates_include_cycles()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "INCLUDE a.ink\n");
        dir.Write("a.ink", "INCLUDE main.ink\nINCLUDE a.ink\n");

        var project = InkProject.Open(main);

        await Assert.That(project.Files.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Missing_include_is_created_as_dirty_buffer_and_written_on_save()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "Hello");
        var project = InkProject.Open(main);

        project.Main.SetText("INCLUDE chapters/new.ink\nHello");
        var result = project.RefreshIncludes();

        await Assert.That(result.StructureChanged).IsTrue();
        await Assert.That(result.CreatedFiles.Select(f => f.RelativePath)).IsEquivalentTo(["chapters/new.ink"]);
        var created = project.Find("chapters/new.ink")!;
        await Assert.That(created.IsDirty).IsTrue();
        await Assert.That(created.ExistsOnDisk).IsFalse();

        await project.SaveAllAsync();

        await Assert.That(dir.Exists("chapters/new.ink")).IsTrue();
        await Assert.That(created.IsDirty).IsFalse();
    }

    [Test]
    public async Task Include_outside_root_is_reported()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "INCLUDE ../evil.ink\n");

        var project = InkProject.Open(main);

        await Assert.That(project.IncludeIssues).Count().IsEqualTo(1);
        await Assert.That(project.IncludeIssues[0].Line).IsEqualTo(1);
    }

    [Test]
    public async Task Unreadable_include_is_reported_and_retried_on_the_next_scan()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return; // relies on Unix permissions, which root ignores
        }

        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "INCLUDE a.ink\nINCLUDE b.ink\n");
        var a = dir.Write("a.ink", "A");
        dir.Write("b.ink", "B");
        File.SetUnixFileMode(a, UnixFileMode.None);
        try
        {
            var project = InkProject.Open(main);

            await Assert.That(project.Files.Select(f => f.RelativePath)).IsEquivalentTo(["main.ink", "b.ink"]);
            await Assert.That(project.IncludeIssues).Count().IsEqualTo(1);
            await Assert.That(project.IncludeIssues[0].Line).IsEqualTo(1);

            File.SetUnixFileMode(a, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            project.RefreshIncludes();

            await Assert.That(project.Find("a.ink")?.Text).IsEqualTo("A");
            await Assert.That(project.IncludeIssues).IsEmpty();
        }
        finally
        {
            File.SetUnixFileMode(a, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Test]
    public async Task Removing_an_include_drops_the_file()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "INCLUDE a.ink\n");
        dir.Write("a.ink", "A");
        var project = InkProject.Open(main);

        project.Main.SetText("No includes");
        var result = project.RefreshIncludes();

        await Assert.That(result.StructureChanged).IsTrue();
        await Assert.That(project.Find("a.ink")).IsNull();
        await Assert.That(project.FindUnincludedFiles()).IsEquivalentTo(["a.ink"]);
    }

    [Test]
    public async Task Dirty_file_that_is_no_longer_included_is_still_tracked_and_saved()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "INCLUDE a.ink\n");
        dir.Write("a.ink", "A");
        var project = InkProject.Open(main);
        var a = project.Find("a.ink")!;

        a.SetText("A edited");
        project.Main.SetText("No includes");
        project.RefreshIncludes();

        await Assert.That(project.Find("a.ink")).IsNull();
        await Assert.That(project.DirtyFiles.Select(f => f.RelativePath)).IsEquivalentTo(
            ["main.ink", "a.ink"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

        var saved = await project.SaveAllAsync();

        await Assert.That(saved).Contains(a);
        await Assert.That(dir.Read("a.ink")).IsEqualTo("A edited");
        await Assert.That(project.DirtyFiles).IsEmpty();
    }

    [Test]
    public async Task Save_preserves_crlf_and_bom()
    {
        using var dir = new TempDirectory();
        var main = dir.Write("main.ink", "Line one\nLine two\n", crlf: true, bom: true);
        var project = InkProject.Open(main);

        await Assert.That(project.Main.Text).IsEqualTo("Line one\nLine two\n");
        await Assert.That(project.Main.Format).IsEqualTo(new TextFormat(LineEnding.CrLf, HasBom: true));

        project.Main.SetText("Line one\nLine two\nLine three\n");
        await project.SaveAsync(project.Main);

        var bytes = dir.ReadBytes("main.ink");
        await Assert.That(bytes.Take(3).ToArray()).IsEquivalentTo(new byte[] { 0xEF, 0xBB, 0xBF });
        await Assert.That(System.Text.Encoding.UTF8.GetString(bytes[3..])).IsEqualTo("Line one\r\nLine two\r\nLine three\r\n");
        await Assert.That(project.Main.IsDirty).IsFalse();
        await Assert.That(project.Main.DiskHash).IsEqualTo(TextFileCodec.Hash(bytes));
    }

    [Test]
    public async Task Editing_back_to_saved_text_clears_dirty()
    {
        using var dir = new TempDirectory();
        var project = InkProject.Open(dir.Write("main.ink", "abc"));

        project.Main.SetText("abcd");
        await Assert.That(project.Main.IsDirty).IsTrue();
        project.Main.SetText("abc");
        await Assert.That(project.Main.IsDirty).IsFalse();
        await Assert.That(project.Main.Version).IsEqualTo(2);
    }

    [Test]
    public async Task Snapshot_is_isolated_from_later_edits()
    {
        using var dir = new TempDirectory();
        var project = InkProject.Open(dir.Write("main.ink", "one"));

        var snapshot = project.CreateSnapshot();
        project.Main.SetText("two");

        await Assert.That(snapshot.MainText).IsEqualTo("one");
        await Assert.That(project.CreateSnapshot().MainText).IsEqualTo("two");
    }
}
