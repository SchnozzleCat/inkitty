using Inkitty.Core.FileSystem;

namespace Inkitty.Core.Projects;

/// <summary>
/// One ink file in a project. Owned by the UI thread; background work only ever sees
/// immutable <see cref="ProjectSnapshot"/>s.
/// </summary>
public sealed class InkSourceFile
{
    private string _savedText;

    internal InkSourceFile(string relativePath, string fullPath, string text, TextFormat format, string? diskHash)
    {
        RelativePath = relativePath;
        FullPath = fullPath;
        Text = text;
        _savedText = text;
        Format = format;
        DiskHash = diskHash;
    }

    /// <summary>Canonical project-relative path, e.g. <c>chapters/forest.ink</c>.</summary>
    public string RelativePath { get; }

    public string FullPath { get; }

    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>Current buffer contents, always with <c>\n</c> line endings.</summary>
    public string Text { get; private set; }

    /// <summary>Monotonic version, incremented on every change to <see cref="Text"/>.</summary>
    public int Version { get; private set; }

    public bool IsDirty { get; private set; }

    /// <summary>Line ending and BOM used on disk, preserved on save.</summary>
    public TextFormat Format { get; private set; }

    /// <summary>Hash of the bytes we last read from or wrote to disk; null if not on disk.</summary>
    public string? DiskHash { get; private set; }

    public bool ExistsOnDisk => DiskHash is not null;

    /// <summary>The file changed on disk while it had unsaved edits and the user chose to keep theirs.</summary>
    public bool IsInConflict { get; set; }

    /// <summary>Raised when <see cref="Text"/>, <see cref="IsDirty"/> or disk state changes.</summary>
    public event EventHandler? Changed;

    public void SetText(string text)
    {
        var normalized = TextFileCodec.NormalizeNewlines(text);
        if (string.Equals(normalized, Text, StringComparison.Ordinal))
        {
            return;
        }

        Text = normalized;
        Version++;
        IsDirty = !string.Equals(normalized, _savedText, StringComparison.Ordinal);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the buffer with what is on disk (reload).</summary>
    public void LoadFromDisk(ReadOnlySpan<byte> bytes)
    {
        var (text, format) = TextFileCodec.Decode(bytes);
        DiskHash = TextFileCodec.Hash(bytes);
        Format = format;
        _savedText = text;
        IsInConflict = false;
        if (!string.Equals(text, Text, StringComparison.Ordinal))
        {
            Text = text;
            Version++;
        }

        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Bytes to write for a save, in the file's original format.</summary>
    public byte[] Encode() => TextFileCodec.Encode(Text, Format);

    /// <summary>Records a successful save of <paramref name="writtenBytes"/> taken from <paramref name="savedText"/>.</summary>
    public void MarkSaved(string savedText, ReadOnlySpan<byte> writtenBytes)
    {
        _savedText = savedText;
        DiskHash = TextFileCodec.Hash(writtenBytes);
        IsInConflict = false;
        IsDirty = !string.Equals(Text, savedText, StringComparison.Ordinal);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The file was deleted on disk; the buffer is kept and becomes dirty.</summary>
    public void MarkDeletedOnDisk()
    {
        DiskHash = null;
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public override string ToString() => RelativePath;
}
