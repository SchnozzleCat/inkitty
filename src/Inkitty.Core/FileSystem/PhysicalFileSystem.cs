namespace Inkitty.Core.FileSystem;

public sealed class PhysicalFileSystem : IFileSystem
{
    private const int ReadAttempts = 3;

    public static PhysicalFileSystem Instance { get; } = new();

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public byte[] ReadAllBytes(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException) when (attempt < ReadAttempts && File.Exists(path))
            {
                // Another editor may briefly hold the file open (common on Windows).
                Thread.Sleep(50 * attempt);
            }
        }
    }

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(path, cancellationToken);
            }
            catch (IOException) when (attempt < ReadAttempts && File.Exists(path))
            {
                await Task.Delay(50 * attempt, cancellationToken);
            }
        }
    }

    public async Task WriteAllBytesAtomicAsync(string path, byte[] contents, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(directory);
        var temp = Path.Join(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, contents, cancellationToken);
            if (OperatingSystem.IsWindows() && File.Exists(path))
            {
                File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, searchPattern, new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            })
            : [];
}
