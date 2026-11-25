using Avalonia.VisualTree;
using Inkitty.App.Controls.InkEditor;
using Inkitty.App.Tests.Infrastructure;
using Inkitty.App.ViewModels;
using Inkitty.App.Views;
using Inkitty.Core.Compilation;
using Microsoft.Extensions.DependencyInjection;

namespace Inkitty.App.Tests;

[NotInParallel("ui")]
public class MainWindowTests
{
    [Test]
    public async Task Opening_the_sample_compiles_plays_and_shows_the_editor()
    {
        await HeadlessSession.Run(async () =>
        {
            var main = UiTestHelpers.CopySample("multi-file", out _);
            var vm = App.Services!.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };
            window.Show();

            await vm.OpenProjectAsync(main);
            await Assert.That(await UiTestHelpers.WaitUntil(() => vm.Project?.LastResult is not null)).IsTrue();
            await Assert.That(vm.Project!.LastResult).IsTypeOf<CompileSucceeded>();

            var player = vm.Factory.PlayerTool;
            await Assert.That(await UiTestHelpers.WaitUntil(() => player.Choices.Count > 0)).IsTrue();
            await Assert.That(player.Items.Count).IsGreaterThan(0);

            var editor = window.GetVisualDescendants().OfType<InkEditor>().FirstOrDefault();
            await Assert.That(editor).IsNotNull();
            await Assert.That(editor!.TextEditor.Document.Text).Contains("INCLUDE chapters/forest.ink");

            await vm.CloseProjectAsync();
            window.Close();
        });
    }
}
