using System.Collections.Concurrent;
using Inkitty.Core.FileSystem;
using Inkitty.Core.Tests.Infrastructure;

namespace Inkitty.Core.Tests.FileSystem;

[NotInParallel("fs")]
public class ProjectWatcherTests
{
    private static async Task<IReadOnlyCollection<string>> NextBatch(ConcurrentQueue<IReadOnlyCollection<string>> batches, int timeoutMs = 5000)
    {
        for (var waited = 0; waited < timeoutMs; waited += 20)
        {
            if (batches.TryDequeue(out var batch))
            {
                return batch;
            }

            await Task.Delay(20);
        }

        return [];
    }

    [Test]
    public async Task Reports_external_modification()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("main.ink", "one");
        await using var watcher = new ProjectWatcher(dir.Path, coalesce: TimeSpan.FromMilliseconds(100));
        var batches = new ConcurrentQueue<IReadOnlyCollection<string>>();
        watcher.FilesChanged += (_, b) => batches.Enqueue(b);

        await Task.Delay(100);
        dir.Write("main.ink", "two");

        var batch = await NextBatch(batches);
        await Assert.That(batch).Contains(Path.GetFullPath(path));
    }

    [Test]
    public async Task Coalesces_atomic_save_into_one_batch()
    {
        using var dir = new TempDirectory();
        dir.Write("main.ink", "one");
        dir.Write("other.ink", "one");
        await using var watcher = new ProjectWatcher(dir.Path, coalesce: TimeSpan.FromMilliseconds(300));
        var batches = new ConcurrentQueue<IReadOnlyCollection<string>>();
        watcher.FilesChanged += (_, b) => batches.Enqueue(b);

        await Task.Delay(100);
        await PhysicalFileSystem.Instance.WriteAllBytesAtomicAsync(Path.Join(dir.Path, "main.ink"), "two"u8.ToArray());
        // Files created inside brand-new directories can be missed by inotify before it watches the
        // directory; the window-focus rescan covers that, so only existing directories are tested here.
        dir.Write("other.ink", "two");

        var batch = await NextBatch(batches);
        await Assert.That(batch.Count).IsEqualTo(2);
        await Task.Delay(500);
        await Assert.That(batches).IsEmpty();
    }

    [Test]
    public async Task Ignores_non_ink_and_temp_files()
    {
        using var dir = new TempDirectory();
        await using var watcher = new ProjectWatcher(dir.Path, coalesce: TimeSpan.FromMilliseconds(100));
        var batches = new ConcurrentQueue<IReadOnlyCollection<string>>();
        watcher.FilesChanged += (_, b) => batches.Enqueue(b);

        dir.Write("notes.txt", "x");
        dir.Write(".main.ink.123.tmp", "x");

        var batch = await NextBatch(batches, timeoutMs: 600);
        await Assert.That(batch).IsEmpty();
    }
}
