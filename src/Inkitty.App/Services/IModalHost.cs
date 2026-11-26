using Avalonia.Controls;

namespace Inkitty.App.Services;

/// <summary>Shows modal content: a dialog window on desktop, an overlay where there are no OS windows.</summary>
public interface IModalHost
{
    Task ShowModalAsync(string title, Control content);

    void CloseModal();
}
