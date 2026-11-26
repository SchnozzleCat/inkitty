namespace Inkitty.Core.Playback;

/// <summary>Where a piece of story output came from.</summary>
/// <param name="File">Project-relative file, if known.</param>
/// <param name="Line">1-based line in <paramref name="File"/>.</param>
/// <param name="RuntimePath">ink runtime path of the content (e.g. <c>forest.clearing.2</c>), for mapping to knots/stitches.</param>
public sealed record StoryLocation(string? File, int Line, string? RuntimePath);
