using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public partial class StashPickViewModel : ViewModelBase
{
    public StashPickViewModel(
        string title,
        string subtitle,
        string confirmText,
        IReadOnlyList<FileChange> files,
        bool showMessage)
    {
        Title = title;
        Subtitle = subtitle;
        ConfirmText = confirmText;
        ShowMessage = showMessage;
        IsEmpty = files.Count == 0;
        foreach (var file in files)
        {
            var item = new WorktreeImportItem(file.DisplayPath, file.StatusLabel, file.Path);
            item.PropertyChanged += OnItemChanged;
            Files.Add(item);
        }

        Summary = BuildSummary();
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string ConfirmText { get; }
    public bool ShowMessage { get; }
    public bool IsEmpty { get; }
    public bool HasFiles => Files.Count > 0;
    public ObservableCollection<WorktreeImportItem> Files { get; } = [];
    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;

    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _summary = "";

    public bool CanConfirm => Files.Any(f => f.IsSelected);

    public IReadOnlyList<string> SelectedPaths =>
        Files.Where(f => f.IsSelected && f.Path is not null).Select(f => f.Path!).ToList();

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in Files)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in Files)
            item.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Confirmed = true;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Confirmed = false;
        CloseRequested?.Invoke();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorktreeImportItem.IsSelected))
            return;
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
        Summary = BuildSummary();
    }

    private string BuildSummary()
    {
        var count = Files.Count(f => f.IsSelected);
        if (count == 0)
            return "Nothing selected.";
        if (count == Files.Count)
            return count == 1 ? "1 file selected." : $"All {count} files selected.";
        return count == 1 ? "1 file selected." : $"{count} files selected.";
    }
}
