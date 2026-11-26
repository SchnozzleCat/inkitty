using Avalonia.Controls;
using Inkitty.App.ViewModels.Tools;

namespace Inkitty.App.Views.Tools;

public partial class StoryMapToolView : UserControl
{
    private StoryMapToolViewModel? _viewModel;

    public StoryMapToolView()
    {
        InitializeComponent();
        Map.NodeActivated += (_, id) => _viewModel?.OpenNode(id);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.ViewRequested -= OnViewRequested;
        }

        _viewModel = DataContext as StoryMapToolViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ViewRequested += OnViewRequested;
        }
    }

    private void OnViewRequested(object? sender, string? nodeId)
    {
        if (nodeId is null)
        {
            Map.FitToView();
        }
        else
        {
            Map.CenterOn(nodeId);
        }
    }
}
