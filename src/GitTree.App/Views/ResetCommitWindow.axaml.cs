using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class ResetCommitWindow : Window
{
    public ResetCommitWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is ResetCommitViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}
