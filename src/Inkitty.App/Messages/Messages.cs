using Inkitty.Core.Symbols;

namespace Inkitty.App.Messages;

/// <summary>Open (or activate) a file and move the caret to a range.</summary>
/// <param name="File">Project-relative path.</param>
/// <param name="Range">Where to go; null just opens the file.</param>
/// <param name="RecordHistory">Whether to push the current location onto the back stack.</param>
public sealed record NavigateToSourceMessage(string File, SourceRange? Range, bool RecordHistory = true);

/// <summary>Briefly show a message in the status bar.</summary>
public sealed record StatusMessage(string Text);
