using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class RemoteCheckoutConflictWindow : Window
{
    public RemoteCheckoutConflictWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is RemoteCheckoutConflictViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}
