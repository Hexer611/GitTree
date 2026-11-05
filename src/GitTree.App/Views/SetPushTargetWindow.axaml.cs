using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class SetPushTargetWindow : Window
{
    public SetPushTargetWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is SetPushTargetViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}