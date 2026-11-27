namespace Inkitty.App.Input;

/// <summary>A view that is reading raw keys (recording a shortcut), so Esc mustn't close its dialog.</summary>
public interface IKeyCapture
{
    bool IsCapturingKeys { get; }
}
