using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GitTree.App.Theming;
using GitTree.App.ViewModels;
using GitTree.App.Views;

namespace GitTree.App;

public partial class App : Application
{
    public override void Initialize()
    {
        Control.RequestBringIntoViewEvent.AddClassHandler<TreeViewItem>(OnTreeViewItemBringIntoView);
        AvaloniaXamlLoader.Load(this);
    }

    private static void OnTreeViewItemBringIntoView(TreeViewItem item, RequestBringIntoViewEventArgs e)
    {
        e.TargetRect = e.TargetRect.WithWidth(0);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ThemeManager.ApplySaved();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}