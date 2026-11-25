namespace Inkitty.Core.FileSystem;

/// <summary>Thin, testable abstraction over the bits of the file system Inkitty uses.</summary>
public interface IFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    byte[] ReadAllBytes(string path);

    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Writes the file atomically: a temp file in the same directory replaces the target.</summary>
    Task WriteAllBytesAtomicAsync(string path, byte[] contents, CancellationToken cancellationToken = default);

    void CreateDirectory(string path);

    IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive);
}
