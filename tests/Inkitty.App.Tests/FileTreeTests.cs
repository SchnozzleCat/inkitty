using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class FileTreeTests
{
    private static string Shape(IEnumerable<FileNode> nodes, int depth = 0) =>
        string.Concat(nodes.Where(n => n.IsFolder || n.IsFile || n.IsUnincluded || n.IsHeader).Select(n =>
            $"{new string(' ', depth * 2)}{n.Label}{(n.IsFolder ? "/" : string.Empty)}\n" + Shape(n.Children, depth + 1)));

    [Test]
    public async Task Files_are_grouped_by_folder() =>
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out var root);
            Directory.CreateDirectory(Path.Join(root, "chapters", "act2"));
            File.WriteAllText(Path.Join(root, "chapters", "act2", "castle.ink"), "=== castle ===\n-> END\n");
            File.WriteAllText(Path.Join(root, "chapters", "town.ink"), File.ReadAllText(Path.Join(root, "chapters", "town.ink")) + "\nINCLUDE chapters/act2/castle.ink\n");
            Directory.CreateDirectory(Path.Join(root, "notes"));
            File.WriteAllText(Path.Join(root, "notes", "ideas.ink"), "Some idea\n");

            await using var app = AppFixture.Create();
            await app.OpenAsync(main);
            var tree = app.Shell.Factory.FilesTool;
            await UiTestHelpers.WaitUntil(() => tree.Nodes.Any(n => n.IsHeader));

            await Assert.That(Shape(tree.Nodes)).IsEqualTo("""
                chapters/
                  act2/
                    castle.ink
                  forest.ink
                  town.ink
                main.ink
                Not included
                  notes/
                    ideas.ink

                """.ReplaceLineEndings("\n"));

            var chapters = tree.Nodes.Single(n => n.IsFolder);
            await Assert.That(chapters.IsExpanded).IsTrue();
            await Assert.That(chapters.Children.Single(n => n.Label == "forest.ink").File).IsEqualTo("chapters/forest.ink");
        });

    [Test]
    public async Task Collapsed_folders_and_errors_survive_recompiles() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var tree = app.Shell.Factory.FilesTool;
            tree.Nodes.Single(n => n.IsFolder).IsExpanded = false;

            // Break forest.ink: the folder shows the error even while collapsed.
            var forest = app.Shell.OpenDocument(app.Project.Project.Find("chapters/forest.ink")!);
            await app.WaitForCompileAfter(() => forest.Document.Insert(forest.Document.TextLength, "\n-> nowhere\n"));

            var chapters = tree.Nodes.Single(n => n.IsFolder);
            await Assert.That(chapters.IsExpanded).IsFalse();
            await Assert.That(chapters.HasErrors).IsTrue();
            await Assert.That(chapters.Children.Single(n => n.Label == "forest.ink").IsDirty).IsTrue();
        });

    [Test]
    public async Task Opening_a_file_from_a_folder_opens_it_in_the_editor() =>
        await HeadlessSession.Run(async () =>
        {
            await using var app = AppFixture.Create();
            await app.OpenAsync(UiTestHelpers.CopySample("multi-file", out _));
            var tree = app.Shell.Factory.FilesTool;
            var town = tree.Nodes.Single(n => n.IsFolder).Children.Single(n => n.Label == "town.ink");

            tree.OpenCommand.Execute(town);
            await UiTestHelpers.WaitUntil(() => app.Shell.ActiveDocument?.RelativePath == "chapters/town.ink");

            await Assert.That(app.Shell.ActiveDocument?.RelativePath).IsEqualTo("chapters/town.ink");
        });
}
