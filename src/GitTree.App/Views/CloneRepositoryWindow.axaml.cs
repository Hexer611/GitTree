using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class CloneRepositoryWindow : Window
{
    public CloneRepositoryWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is CloneRepositoryViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}
