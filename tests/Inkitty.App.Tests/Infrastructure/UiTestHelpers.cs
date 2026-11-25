using Avalonia.Headless;
using Avalonia.Threading;

namespace Inkitty.App.Tests.Infrastructure;

public static class UiTestHelpers
{
    /// <summary>Pumps the dispatcher until <paramref name="condition"/> holds (background work posts back to the UI thread).</summary>
    public static async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(15);
        }

        Dispatcher.UIThread.RunJobs();
        return true;
    }

    public static string FindSample(string relative)
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

    /// <summary>Copies a sample project to a temp directory so tests can modify it.</summary>
    public static string CopySample(string folder, out string root)
    {
        var source = Path.GetDirectoryName(FindSample(Path.Join(folder, "main.ink")))!;
        root = Path.Join(Path.GetTempPath(), "inkitty-app-tests", Guid.NewGuid().ToString("N"));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Join(root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return Path.Join(root, "main.ink");
    }
}
