using Inkitty.Core.Projects;

namespace Inkitty.Core.Tests.Infrastructure;

/// <summary>Builds snapshots directly from strings, without touching disk.</summary>
public static class InMemoryProject
{
    public static string Root { get; } = OperatingSystem.IsWindows() ? @"C:\inkitty-virtual" : "/inkitty-virtual";

    public static ProjectSnapshot Snapshot(params (string Path, string Text)[] files) =>
        new(Root, files[0].Path, files.Select(f => (f.Path, f.Text, 0)));

    public static ProjectSnapshot OneFile(string text) => Snapshot(("main.ink", text));
}
