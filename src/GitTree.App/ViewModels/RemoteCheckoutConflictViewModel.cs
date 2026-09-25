using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public partial class RemoteCheckoutConflictViewModel : ViewModelBase
{
    public RemoteCheckoutConflictViewModel(RemoteCheckoutPlan plan)
    {
        Summary = plan.Summary;
        Options = RemoteCheckout.ConflictOptions(plan);
        _selectedOption = RemoteCheckout.DefaultOption(plan, Options);
    }

    public string Summary { get; }
    public IReadOnlyList<RemoteCheckoutOption> Options { get; }
    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;

    private RemoteCheckoutOption? _selectedOption;

    public RemoteCheckoutOption? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (value is null || !SetProperty(ref _selectedOption, value))
                return;
            OnPropertyChanged(nameof(ConfirmText));
            OnPropertyChanged(nameof(IsDestructive));
            OnPropertyChanged(nameof(SelectedDescription));
        }
    }

    public string ConfirmText => SelectedOption?.Action switch
    {
        RemoteCheckoutConflictAction.ReplaceLocal => "Replace & checkout",
        RemoteCheckoutConflictAction.CheckoutLocal => "Checkout local",
        RemoteCheckoutConflictAction.DetachAtRemote => "Detach HEAD",
        _ => "Checkout"
    };

    public bool IsDestructive => SelectedOption?.IsDestructive == true;
    public string SelectedDescription => SelectedOption?.Description ?? "";

    [RelayCommand]
    private void Confirm()
    {
        if (SelectedOption is null)
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
