using System.Text;

namespace Inkitty.Core.Tests.Infrastructure;

/// <summary>A throwaway directory with helpers to lay out ink projects.</summary>
public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Join(System.IO.Path.GetTempPath(), "inkitty-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Write(string relativePath, string contents, bool crlf = false, bool bom = false)
    {
        var full = System.IO.Path.Join(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        var text = crlf ? contents.Replace("\n", "\r\n", StringComparison.Ordinal) : contents;
        byte[] bytes = [.. bom ? new byte[] { 0xEF, 0xBB, 0xBF } : [], .. Encoding.UTF8.GetBytes(text)];
        File.WriteAllBytes(full, bytes);
        return full;
    }

    public string Read(string relativePath) => File.ReadAllText(System.IO.Path.Join(Path, relativePath));

    public byte[] ReadBytes(string relativePath) => File.ReadAllBytes(System.IO.Path.Join(Path, relativePath));

    public bool Exists(string relativePath) => File.Exists(System.IO.Path.Join(Path, relativePath));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
