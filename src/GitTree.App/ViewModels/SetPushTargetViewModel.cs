using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public sealed class PushRemoteChoice
{
    public required RemoteInfo Remote { get; init; }
    public string Name => Remote.Name;
    public override string ToString() => Name;
}

public partial class SetPushTargetViewModel : ViewModelBase
{
    private readonly string _localBranch;
    private readonly BranchPushTarget? _current;
    private readonly IReadOnlyList<BranchRef> _branches;
    private readonly IReadOnlyList<RemoteInfo> _remotes;

    public SetPushTargetViewModel(
        BranchRef branch,
        IReadOnlyList<RemoteInfo> remotes,
        IReadOnlyList<BranchRef> branches,
        BranchPushTarget? current)
    {
        _localBranch = branch.Name;
        _current = current;
        _branches = branches;
        _remotes = remotes;
        LocalBranch = branch.Name;
        HasCurrentTarget = current is not null;
        CurrentTarget = current is null ? "No push target" : current.Value.Display;
        Remotes = remotes.Select(remote => new PushRemoteChoice { Remote = remote }).ToList();
        _selectedRemote = Remotes.FirstOrDefault(remote =>
                current is not null && remote.Name.Equals(current.Value.Remote, StringComparison.OrdinalIgnoreCase))
            ?? Remotes.FirstOrDefault(remote => remote.Name.Equals("origin", StringComparison.OrdinalIgnoreCase))
            ?? Remotes[0];
        RebuildSuggestions();
        _remoteBranch = DefaultBranchFor(_selectedRemote);
    }

    public string LocalBranch { get; }
    public string CurrentTarget { get; }
    public bool HasCurrentTarget { get; }
    public IReadOnlyList<PushRemoteChoice> Remotes { get; }
    public ObservableCollection<string> BranchSuggestions { get; } = [];
    public bool Confirmed { get; private set; }
    public bool ClearTarget { get; private set; }
    public event Action? CloseRequested;

    [ObservableProperty] private PushRemoteChoice? _selectedRemote;
    [ObservableProperty] private string _remoteBranch = "";
    [ObservableProperty] private string _errorMessage = "";

    partial void OnSelectedRemoteChanged(PushRemoteChoice? value)
    {
        RebuildSuggestions();
        RemoteBranch = DefaultBranchFor(value);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedRemote is null)
        {
            ErrorMessage = "Choose a remote.";
            return;
        }

        var branch = RemoteBranch.Trim();
        var error = BranchRefNames.Validate(branch);
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }

        RemoteBranch = branch;
        ClearTarget = false;
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        ClearTarget = true;
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        CloseRequested?.Invoke();
    }

    private string DefaultBranchFor(PushRemoteChoice? remote)
    {
        if (remote is not null
            && _current is not null
            && remote.Name.Equals(_current.Value.Remote, StringComparison.OrdinalIgnoreCase))
            return _current.Value.Branch;
        return _localBranch;
    }

    private void RebuildSuggestions()
    {
        BranchSuggestions.Clear();
        if (SelectedRemote is null)
            return;

        var names = new List<string>();
        foreach (var branch in _branches)
        {
            if (!branch.IsRemote)
                continue;
            var split = PullRequestUrls.SplitRemoteRef(branch.Name, _remotes);
            if (!split.Remote.Equals(SelectedRemote.Name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!names.Contains(split.Branch, StringComparer.Ordinal))
                names.Add(split.Branch);
        }

        var preferred = DefaultBranchFor(SelectedRemote);
        if (!names.Contains(preferred, StringComparer.Ordinal))
            names.Insert(0, preferred);
        if (!names.Contains(_localBranch, StringComparer.Ordinal))
            names.Add(_localBranch);

        foreach (var name in names)
            BranchSuggestions.Add(name);
    }
}
