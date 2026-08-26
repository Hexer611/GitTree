using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GitTree.App.Theming;
using GitTree.App.ViewModels;
using GitTree.Core;
using System.ComponentModel;

namespace GitTree.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        Opened += (_, _) =>
        {
            WindowPlacement.Restore(this);
            if (DataContext is MainViewModel vm)
            {
                vm.Host = this;
                vm.PropertyChanged += OnViewModelPropertyChanged;
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
        Closing += (_, _) => WindowPlacement.Save(this);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasRepo) && sender is MainViewModel { HasRepo: true })
            Dispatcher.UIThread.Post(() => WindowPlacement.RestorePanes(this), DispatcherPriority.Loaded);
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

    private void OnUnstagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && DataContext is MainViewModel vm)
            vm.SetUnstagedSelection(list.SelectedItems?.OfType<FileChange>() ?? []);
    }

    private void OnStagedSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && DataContext is MainViewModel vm)
            vm.SetStagedSelection(list.SelectedItems?.OfType<FileChange>() ?? []);
    }

    private void OnSelectAllUnstaged(object? sender, RoutedEventArgs e) =>
        SelectAllFiles("UnstagedList", (vm, files) => vm.SetUnstagedSelection(files));

    private void OnSelectAllStaged(object? sender, RoutedEventArgs e) =>
        SelectAllFiles("StagedList", (vm, files) => vm.SetStagedSelection(files));

    private void SelectAllFiles(string listName, Action<MainViewModel, IEnumerable<FileChange>> apply)
    {
        var list = this.FindControl<ListBox>(listName);
        if (list is null || DataContext is not MainViewModel vm)
            return;

        list.SelectAll();
        var files = list.SelectedItems?.OfType<FileChange>().ToList() ?? [];
        apply(vm, files);
        if (files.Count > 0)
        {
            if (listName == "UnstagedList")
                vm.SelectedUnstaged = files[0];
            else
                vm.SelectedStaged = files[0];
        }
    }
}
