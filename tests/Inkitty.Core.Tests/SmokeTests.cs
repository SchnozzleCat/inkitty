namespace Inkitty.Core.Tests;

public class SmokeTests
{
    [Test]
    public async Task Ink_compiler_compiles_hello_world()
    {
        var compiler = new Ink.Compiler("Hello, world!", new Ink.Compiler.Options { errorHandler = (_, _) => { } });
        var story = compiler.Compile();

        await Assert.That(story).IsNotNull();
        await Assert.That(story!.Continue()).IsEqualTo("Hello, world!\n");
    }
}
