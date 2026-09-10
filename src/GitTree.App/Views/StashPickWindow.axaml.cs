using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class StashPickWindow : Window
{
    public StashPickWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is StashPickViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}
