using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitTree.App.ViewModels;

namespace GitTree.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Host = this;
                vm.CommitRevealed += commit =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        var list = this.FindControl<ListBox>("CommitList");
                        list?.ScrollIntoView(commit);
                    });
                };
            }
        };
    }

    private void OnLocalTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (SelectTreeNode(sender, e) is { } node && DataContext is MainViewModel vm)
            vm.SelectedLocalNode = node;
    }

    private void OnWorktreeTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (SelectTreeNode(sender, e) is { } node && DataContext is MainViewModel vm)
            vm.SelectedWorktreeNode = node;
    }

    private static RefTreeNode? SelectTreeNode(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TreeView tree)
            return null;

        var visual = e.Source as Visual;
        var item = visual?.FindAncestorOfType<TreeViewItem>(includeSelf: true);
        if (item?.DataContext is not RefTreeNode node)
            return null;

        tree.SelectedItem = node;
        return node;
    }
}
