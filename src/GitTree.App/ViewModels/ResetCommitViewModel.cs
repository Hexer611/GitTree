using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public sealed class ResetModeOption
{
    public ResetModeOption(ResetMode mode, string label)
    {
        Mode = mode;
        Label = label;
    }

    public ResetMode Mode { get; }
    public string Label { get; }

    public override string ToString() => Label;
}

public partial class ResetCommitViewModel : ViewModelBase
{
    public ResetCommitViewModel(string branchName, CommitNode commit)
    {
        BranchName = branchName;
        var sha = commit.Sha.Length >= 10 ? commit.Sha[..10] : commit.Sha;
        CommitLabel = $"{sha}: {commit.Subject}";
        SelectedMode = Modes[1];
    }

    public string BranchName { get; }
    public string CommitLabel { get; }

    public IReadOnlyList<ResetModeOption> Modes { get; } =
    [
        new(ResetMode.Soft, "Soft - keep all local changes"),
        new(ResetMode.Mixed, "Mixed - keep working copy but reset index"),
        new(ResetMode.Hard, "Hard - discard all working copy changes")
    ];

    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;

    private ResetModeOption _selectedMode = null!;

    public ResetModeOption SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value is null || !SetProperty(ref _selectedMode, value))
                return;
            OnPropertyChanged(nameof(IsHard));
        }
    }

    public bool IsHard => SelectedMode.Mode == ResetMode.Hard;

    [RelayCommand]
    private void Confirm()
    {
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
