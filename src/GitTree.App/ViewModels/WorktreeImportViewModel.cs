using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public partial class WorktreeImportItem : ObservableObject
{
    public WorktreeImportItem(string title, string subtitle, string? path)
    {
        Title = title;
        Subtitle = subtitle;
        Path = path;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string? Path { get; }

    [ObservableProperty] private bool _isSelected = true;
}

public partial class WorktreeImportViewModel : ViewModelBase
{
    public WorktreeImportViewModel(WorktreeImportPreview preview)
    {
        SourceLabel = preview.Worktree.Label;
        BranchName = string.IsNullOrWhiteSpace(preview.Worktree.Branch)
            ? preview.Worktree.HeadSha[..Math.Min(7, preview.Worktree.HeadSha.Length)]
            : preview.Worktree.Branch;
        IsEmpty = preview.IsEmpty;
        Commits = preview.Commits;
        MergeBranch = preview.Commits.Count > 0;
        foreach (var file in preview.Files)
        {
            var item = new WorktreeImportItem(file.DisplayPath, file.StatusLabel, file.Path);
            item.PropertyChanged += OnItemChanged;
            Files.Add(item);
        }

        Summary = BuildSummary();
    }

    public string SourceLabel { get; }
    public string BranchName { get; }
    public bool IsEmpty { get; }
    public bool HasCommits => Commits.Count > 0;
    public bool HasFiles => Files.Count > 0;
    public IReadOnlyList<WorktreeImportCommit> Commits { get; }
    public ObservableCollection<WorktreeImportItem> Files { get; } = [];
    public bool Confirmed { get; private set; }
    public event Action? CloseRequested;

    [ObservableProperty] private bool _mergeBranch;
    [ObservableProperty] private string _summary = "";

    public bool CanConfirm => !IsEmpty && (MergeBranch || Files.Any(f => f.IsSelected));

    public WorktreeImportSelection ToSelection() => new()
    {
        MergeBranch = MergeBranch,
        FilePaths = Files.Where(f => f.IsSelected && f.Path is not null).Select(f => f.Path!).ToList()
    };

    [RelayCommand]
    private void SelectAll()
    {
        MergeBranch = HasCommits;
        foreach (var item in Files)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        MergeBranch = false;
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

    partial void OnMergeBranchChanged(bool value) => RefreshCanConfirm();

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WorktreeImportItem.IsSelected))
            return;
        RefreshCanConfirm();
    }

    private void RefreshCanConfirm()
    {
        OnPropertyChanged(nameof(CanConfirm));
        ConfirmCommand.NotifyCanExecuteChanged();
        Summary = BuildSummary();
    }

    private string BuildSummary()
    {
        var fileCount = Files.Count(f => f.IsSelected);
        if (MergeBranch && fileCount > 0)
            return $"Merge {BranchName} into the current branch, then bring {fileCount} uncommitted file(s).";
        if (MergeBranch)
            return $"Merge {BranchName} into the current branch.";
        if (fileCount > 0)
            return $"Bring {fileCount} uncommitted file(s) without merging the branch.";
        return "Nothing selected.";
    }
}
