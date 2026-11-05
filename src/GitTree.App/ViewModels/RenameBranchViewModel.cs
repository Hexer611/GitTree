using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public partial class RenameBranchViewModel : ViewModelBase
{
    public RenameBranchViewModel(string currentName)
    {
        CurrentName = currentName;
        NewName = currentName;
    }

    public string CurrentName { get; }
    public bool Confirmed { get; private set; }
    public string ConfirmedName { get; private set; } = "";
    public event Action? CloseRequested;

    [ObservableProperty] private string _newName;
    [ObservableProperty] private string _errorMessage = "";

    [RelayCommand]
    private void Confirm()
    {
        var name = NewName.Trim();
        var error = BranchRefNames.Validate(name);
        if (error is not null)
        {
            ErrorMessage = error;
            return;
        }

        if (name.Equals(CurrentName, StringComparison.Ordinal))
        {
            ErrorMessage = "Enter a different branch name.";
            return;
        }

        ConfirmedName = name;
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
