using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class WorktreeImportWindow : Window
{
    public WorktreeImportWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is WorktreeImportViewModel vm)
                vm.CloseRequested += Close;
        };
    }
}
