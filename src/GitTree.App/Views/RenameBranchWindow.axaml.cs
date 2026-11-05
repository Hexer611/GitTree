using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class RenameBranchWindow : Window
{
    public RenameBranchWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is RenameBranchViewModel vm)
                vm.CloseRequested += Close;
            Dispatcher.UIThread.Post(() =>
            {
                var box = this.FindControl<TextBox>("NameBox");
                if (box is null)
                    return;
                box.Focus();
                box.SelectAll();
            });
        };
    }
}
