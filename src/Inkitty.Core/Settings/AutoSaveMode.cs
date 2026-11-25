namespace Inkitty.Core.Settings;

public enum AutoSaveMode
{
    /// <summary>Only save on explicit commands.</summary>
    Off,

    /// <summary>Save all dirty files when the window loses focus or the active document changes.</summary>
    OnFocusLost,

    /// <summary>Save a dirty file a short delay after its last edit.</summary>
    AfterDelay,
}
