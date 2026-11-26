using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Dock.Model.Mvvm.Controls;
using Inkitty.App.Docking;
using Inkitty.App.Messages;
using Inkitty.Core.StoryMap;

namespace Inkitty.App.ViewModels.Tools;

/// <summary>
/// The story map: knots and stitches as a flow chart, with the player's current position and the path
/// it took highlighted. Mirrors the player, so map and transcript always show the same playthrough.
/// </summary>
public sealed partial class StoryMapToolViewModel : Tool
{
    private readonly PlayerToolViewModel _player;
    private readonly IMessenger _messenger;

    public StoryMapToolViewModel(PlayerToolViewModel player, IMessenger messenger)
    {
        _player = player;
        _messenger = messenger;
        Id = DockIds.StoryMap;
        Title = "Story Map";
        CanClose = true;
        CanPin = true;
        CanFloat = true;
        _player.PropertyChanged += OnPlayerPropertyChanged;
    }

    public StoryGraph Graph => _player.Graph;

    public StoryProgress Progress => _player.Progress;

    public bool HasGraph => Graph.Nodes.Count > 1;

    /// <summary>Scroll the map to keep the current knot visible as the story moves.</summary>
    [ObservableProperty]
    public partial bool FollowCurrent { get; set; } = true;

    public string Summary
    {
        get
        {
            var knots = Graph.Nodes.Count(n => n.Kind is StoryNodeKind.Knot or StoryNodeKind.Stitch);
            var visited = Graph.Nodes.Count(n => n.Kind is StoryNodeKind.Knot or StoryNodeKind.Stitch && Progress.WasVisited(n.Id));
            return knots == 0 ? string.Empty : $"{visited} of {knots} visited";
        }
    }

    /// <summary>Raised to ask the view to fit the whole map, or centre on the current knot.</summary>
    public event EventHandler<string?>? ViewRequested;

    /// <summary>Opens the knot or stitch in the editor.</summary>
    [RelayCommand]
    public void OpenNode(string id)
    {
        if (Graph.Find(id)?.Range is { } range)
        {
            _messenger.Send(new NavigateToSourceMessage(range.File, range));
        }
    }

    [RelayCommand]
    private void FitToView() => ViewRequested?.Invoke(this, null);

    [RelayCommand]
    private void CenterOnCurrent()
    {
        if (Progress.CurrentNode is { } id)
        {
            ViewRequested?.Invoke(this, id);
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerToolViewModel.Graph):
                OnPropertyChanged(nameof(Graph));
                OnPropertyChanged(nameof(HasGraph));
                OnPropertyChanged(nameof(Summary));
                break;
            case nameof(PlayerToolViewModel.Progress):
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(Summary));
                break;
        }
    }
}
