using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class CreatePullRequestWindow : Window
{
    public CreatePullRequestWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is CreatePullRequestViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}
