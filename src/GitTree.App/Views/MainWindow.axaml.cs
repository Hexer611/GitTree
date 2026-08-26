using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitTree.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.Host = this;
        };
    }
}
