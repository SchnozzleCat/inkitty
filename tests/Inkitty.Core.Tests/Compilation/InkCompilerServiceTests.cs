using Inkitty.Core.Compilation;
using Inkitty.Core.Diagnostics;
using Inkitty.Core.Projects;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.Compilation;

public class InkCompilerServiceTests
{
    private readonly InkCompilerService _compiler = new();

    [Test]
    public async Task Compiles_single_file()
    {
        var result = _compiler.Compile(InMemoryProject.OneFile("Hello, world!"));

        var success = result as CompileSucceeded;
        await Assert.That(success).IsNotNull();
        await Assert.That(success!.Story.Continue()).IsEqualTo("Hello, world!\n");
    }

    [Test]
    public async Task Compiles_multiple_files_from_memory()
    {
        var snapshot = InMemoryProject.Snapshot(
            ("main.ink", "INCLUDE chapters/forest.ink\n-> forest"),
            ("chapters/forest.ink", "=== forest ===\nTrees.\n-> END"));

        var result = _compiler.Compile(snapshot);

        await Assert.That(result).IsTypeOf<CompileSucceeded>();
        await Assert.That(((CompileSucceeded)result).Story.Continue()).IsEqualTo("Trees.\n");
    }

    [Test]
    public async Task Reports_error_with_file_and_line()
    {
        var snapshot = InMemoryProject.Snapshot(
            ("main.ink", "INCLUDE forest.ink\n-> forest"),
            ("forest.ink", "=== forest ===\nTrees.\n-> foresst"));

        var result = _compiler.Compile(snapshot);

        await Assert.That(result).IsTypeOf<CompileFailed>();
        var error = result.Issues.Single(i => i.Severity == IssueSeverity.Error);
        await Assert.That(error.File).IsEqualTo("forest.ink");
        await Assert.That(error.Line).IsEqualTo(3);
        await Assert.That(error.Message).Contains("foresst");
    }

    [Test]
    public async Task Unresolved_divert_error_is_narrowed_to_the_token()
    {
        var result = _compiler.Compile(InMemoryProject.OneFile("Hello\n  -> nowhere"));

        var error = result.Issues.Single(i => i.Severity == IssueSeverity.Error);
        await Assert.That(error.Range!.Value.IsWholeLine).IsFalse();
        await Assert.That(error.Range!.Value.Start.Column).IsEqualTo(6);
        await Assert.That(error.Range!.Value.End.Column).IsEqualTo(13);
    }

    [Test]
    public async Task Collects_todos_as_issues()
    {
        var result = _compiler.Compile(InMemoryProject.OneFile("TODO: write this\nHello"));

        var todo = result.Issues.Single();
        await Assert.That(todo.Severity).IsEqualTo(IssueSeverity.Todo);
        await Assert.That(todo.Message).IsEqualTo("write this");
        await Assert.That(result).IsTypeOf<CompileSucceeded>();
    }

    [Test]
    public async Task Missing_include_is_an_error_not_a_crash()
    {
        var result = _compiler.Compile(InMemoryProject.OneFile("INCLUDE nope.ink\nHello"));

        await Assert.That(result).IsTypeOf<CompileFailed>();
        await Assert.That(result.Issues.Any(i => i.Message.Contains("nope.ink", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task No_state_bleeds_between_compiles()
    {
        var bad = _compiler.Compile(InMemoryProject.OneFile("-> nowhere"));
        var good = _compiler.Compile(InMemoryProject.OneFile("Fine.\n-> END"));

        await Assert.That(bad).IsTypeOf<CompileFailed>();
        await Assert.That(good).IsTypeOf<CompileSucceeded>();
        await Assert.That(good.Issues).IsEmpty();
    }

    [Test]
    public async Task Symbols_are_available_even_when_compilation_fails()
    {
        var result = _compiler.Compile(InMemoryProject.OneFile("=== knot ===\nHi\n-> nowhere"));

        await Assert.That(result).IsTypeOf<CompileFailed>();
        await Assert.That(result.Symbols.FindByName("knot")).IsNotEmpty();
    }

    [Test]
    public async Task Compiles_the_bundled_multi_file_sample_from_disk()
    {
        var main = FindSample("multi-file/main.ink");
        var project = InkProject.Open(main);

        var result = _compiler.Compile(project.CreateSnapshot());

        await Assert.That(result.Issues.Where(i => i.Severity.IsError)).IsEmpty();
        await Assert.That(result).IsTypeOf<CompileSucceeded>();
        await Assert.That(project.Files.Count).IsEqualTo(3);
    }

    [Test]
    public async Task Compiles_the_intercept_sample_without_errors()
    {
        var project = InkProject.Open(FindSample("the-intercept/TheIntercept.ink"));

        var result = _compiler.Compile(project.CreateSnapshot());

        await Assert.That(result.Issues.Where(i => i.Severity.IsError)).IsEmpty();
        await Assert.That(result).IsTypeOf<CompileSucceeded>();
    }

    internal static string FindSample(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Join(dir.FullName, "samples", relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Sample not found", relative);
    }
}
