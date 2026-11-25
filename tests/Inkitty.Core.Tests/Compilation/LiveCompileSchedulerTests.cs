using System.Collections.Concurrent;
using Inkitty.Core.Compilation;
using Inkitty.Core.Projects;
using Inkitty.Core.Tests.Infrastructure;
using Microsoft.Extensions.Time.Testing;

namespace Inkitty.Core.Tests.Compilation;

public class LiveCompileSchedulerTests
{
    private sealed class CountingCompiler : IInkCompilerService
    {
        public ConcurrentQueue<string> Compiled { get; } = new();

        public CompileResult Compile(ProjectSnapshot snapshot, int generation = 0, CancellationToken cancellationToken = default)
        {
            Compiled.Enqueue(snapshot.MainText);
            return new InkCompilerService().Compile(snapshot, generation, cancellationToken);
        }
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    [Test]
    public async Task Debounces_bursts_of_requests_into_one_compile()
    {
        var time = new FakeTimeProvider();
        var compiler = new CountingCompiler();
        var text = "a";
        await using var scheduler = new LiveCompileScheduler(compiler, _ => Task.FromResult(InMemoryProject.OneFile(text)), TimeSpan.FromMilliseconds(400), time);
        var results = new ConcurrentQueue<CompileResult>();
        scheduler.Compiled += (_, r) => results.Enqueue(r);

        for (var i = 0; i < 5; i++)
        {
            text = $"text {i}";
            scheduler.Request();
            time.Advance(TimeSpan.FromMilliseconds(100));
        }

        await Assert.That(compiler.Compiled).IsEmpty();

        time.Advance(TimeSpan.FromMilliseconds(400));
        await WaitUntil(() => !results.IsEmpty);

        await Assert.That(compiler.Compiled).IsEquivalentTo(["text 4"]);
        await Assert.That(results.Single().Generation).IsEqualTo(scheduler.LatestGeneration);
    }

    [Test]
    public async Task Request_now_skips_debounce()
    {
        var time = new FakeTimeProvider();
        await using var scheduler = new LiveCompileScheduler(new InkCompilerService(), _ => Task.FromResult(InMemoryProject.OneFile("Hi")), TimeSpan.FromSeconds(10), time);
        var results = new ConcurrentQueue<CompileResult>();
        scheduler.Compiled += (_, r) => results.Enqueue(r);

        scheduler.RequestNow();
        await WaitUntil(() => !results.IsEmpty);

        await Assert.That(results.Single()).IsTypeOf<CompileSucceeded>();
    }

    [Test]
    public async Task Stale_results_are_dropped()
    {
        var gate = new SemaphoreSlim(0);
        var calls = 0;
        await using var scheduler = new LiveCompileScheduler(
            new InkCompilerService(),
            async ct =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    await gate.WaitAsync(ct); // first compile is slow
                }

                return InMemoryProject.OneFile($"call {calls}");
            },
            TimeSpan.Zero);
        var results = new ConcurrentQueue<CompileResult>();
        scheduler.Compiled += (_, r) => results.Enqueue(r);

        scheduler.RequestNow();
        await WaitUntil(() => Volatile.Read(ref calls) == 1);
        scheduler.RequestNow(); // supersedes the in-flight one
        gate.Release();
        await WaitUntil(() => !results.IsEmpty);
        await Task.Delay(50);

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(results.Single().Generation).IsEqualTo(2);
    }
}
