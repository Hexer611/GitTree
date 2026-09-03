using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class CheckoutCommitWindow : Window
{
    public CheckoutCommitWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is CheckoutCommitViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}
