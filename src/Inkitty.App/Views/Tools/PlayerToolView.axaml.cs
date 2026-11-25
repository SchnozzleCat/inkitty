using Avalonia.Controls;
using Avalonia.Threading;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Views.Tools;

public partial class PlayerToolView : UserControl
{
    private PlayerToolViewModel? _viewModel;

    public PlayerToolView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.TranscriptUpdated -= OnTranscriptUpdated;
        }

        _viewModel = DataContext as PlayerToolViewModel;
        if (_viewModel is not null)
        {
            _viewModel.TranscriptUpdated += OnTranscriptUpdated;
        }
    }

    private void OnTranscriptUpdated(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => Scroller.ScrollToEnd(), DispatcherPriority.Background);
}
