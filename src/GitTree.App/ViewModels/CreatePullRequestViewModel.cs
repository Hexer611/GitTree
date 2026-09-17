using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public sealed class PullRequestRemoteChoice
{
    public required string Label { get; init; }
    public required RemoteInfo Remote { get; init; }
    public required PullRequestHostedRepo Hosted { get; init; }

    public override string ToString() => Label;
}

public partial class CreatePullRequestViewModel : ViewModelBase
{
    public CreatePullRequestViewModel(
        string sourceBranch,
        IReadOnlyList<PullRequestRemoteChoice> remotes,
        IReadOnlyList<string> destinations,
        PullRequestRemoteChoice? selectedRemote,
        string? selectedDestination)
    {
        SourceBranch = sourceBranch;
        Remotes = remotes;
        Destinations = destinations;
        HasDestinations = destinations.Count > 0;
        _selectedRemote = selectedRemote ?? remotes.FirstOrDefault();
        _selectedDestination = selectedDestination ?? destinations.FirstOrDefault();
    }

    public string SourceBranch { get; }
    public IReadOnlyList<PullRequestRemoteChoice> Remotes { get; }
    public IReadOnlyList<string> Destinations { get; }
    public bool HasDestinations { get; }
    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;

    private PullRequestRemoteChoice? _selectedRemote;
    public PullRequestRemoteChoice? SelectedRemote
    {
        get => _selectedRemote;
        set => SetProperty(ref _selectedRemote, value);
    }

    private string? _selectedDestination;
    public string? SelectedDestination
    {
        get => _selectedDestination;
        set => SetProperty(ref _selectedDestination, value);
    }

    public string? CreateUrl
    {
        get
        {
            if (SelectedRemote is null || string.IsNullOrWhiteSpace(SourceBranch))
                return null;
            var dest = HasDestinations ? SelectedDestination : null;
            return PullRequestUrls.Create(SelectedRemote.Hosted, SourceBranch, dest);
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (CreateUrl is null)
            return;
        if (HasDestinations && string.IsNullOrWhiteSpace(SelectedDestination))
            return;

        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        CloseRequested?.Invoke();
    }
}
