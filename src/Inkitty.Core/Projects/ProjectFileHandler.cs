namespace Inkitty.Core.Projects;

/// <summary>
/// Serves ink sources to the compiler from a <see cref="ProjectSnapshot"/> instead of disk,
/// so unsaved editor buffers compile without temp files. Include names resolve relative to the
/// project root, as in inklecate/Inky.
/// </summary>
public sealed class ProjectFileHandler(ProjectSnapshot snapshot) : Ink.IFileHandler
{
    public string ResolveInkFilename(string includeName) =>
        snapshot.ResolveFileName(includeName) ?? includeName;

    public string LoadInkFileContents(string fullFilename) =>
        snapshot.TextOf(fullFilename)
        ?? throw new FileNotFoundException($"'{fullFilename}' is not part of the project.", fullFilename);
}
