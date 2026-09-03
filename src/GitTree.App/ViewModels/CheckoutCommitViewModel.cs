using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public partial class CheckoutCommitViewModel : ViewModelBase
{
    public CheckoutCommitViewModel(CommitNode commit, IReadOnlyList<CheckoutChoice> choices)
    {
        var sha = commit.Sha.Length >= 10 ? commit.Sha[..10] : commit.Sha;
        CommitLabel = $"{sha}: {commit.Subject}";
        Choices = choices;
        _selectedChoice = choices.Count > 0 ? choices[0] : null;
    }

    public string CommitLabel { get; }
    public IReadOnlyList<CheckoutChoice> Choices { get; }
    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;

    private CheckoutChoice? _selectedChoice;

    public CheckoutChoice? SelectedChoice
    {
        get => _selectedChoice;
        set => SetProperty(ref _selectedChoice, value);
    }

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedChoice is null)
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
