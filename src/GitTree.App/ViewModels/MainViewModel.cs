using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.Core;
using GitTree.Git.Cli;
using GitTree.Git.LibGit2;

namespace GitTree.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsStore _settings = new();
    private IGitRepository? _repo;
    private GitRepositoryWatcher? _watcher;
    private bool _suppressWatch;
    private int _watchGate;
    private int _refreshSerial;
    private List<string> _unstagedSelection = [];
    private List<string> _stagedSelection = [];

    public Window? Host { get; set; }

    [ObservableProperty] private string _windowTitle = "GitTree";
    [ObservableProperty] private string _repoPath = "No repository open";
    [ObservableProperty] private string _currentBranch = "";
    [ObservableProperty] private string _statusMessage = "Open a Git repository to get started.";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasRepo;
    [ObservableProperty] private bool _isMerging;
    [ObservableProperty] private bool _isRebasing;
    [ObservableProperty] private string _commitMessage = "";
    [ObservableProperty] private string _newBranchName = "";
    [ObservableProperty] private string _conflictFileText = "";
    [ObservableProperty] private string _diffHeader = "Diff";
    [ObservableProperty] private bool _showConflictEditor;
    [ObservableProperty] private int _unstagedCount;
    [ObservableProperty] private int _stagedCount;
    [ObservableProperty] private int _conflictCount;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _hasRecents;
    [ObservableProperty] private int _localCount;
    [ObservableProperty] private int _remoteCount;
    [ObservableProperty] private int _tagCount;
    [ObservableProperty] private int _stashCount;
    [ObservableProperty] private int _worktreeCount;
    [ObservableProperty] private bool _isShowingCommitFiles;
    [ObservableProperty] private bool _needsRefresh;

    public ObservableCollection<string> RecentRepositories { get; } = [];
    public ObservableCollection<CommitNode> Commits { get; } = [];
    public ObservableCollection<FileChange> Unstaged { get; } = [];
    public ObservableCollection<FileChange> Staged { get; } = [];
    public ObservableCollection<FileChange> Conflicts { get; } = [];
    public ObservableCollection<FileChange> CommitFiles { get; } = [];
    public ObservableCollection<RefTreeNode> LocalTree { get; } = [];
    public ObservableCollection<RefTreeNode> RemoteTree { get; } = [];
    public ObservableCollection<RefTreeNode> TagTree { get; } = [];
    public ObservableCollection<RefTreeNode> StashTree { get; } = [];
    public ObservableCollection<RefTreeNode> WorktreeTree { get; } = [];
    public ObservableCollection<DiffLine> DiffLines { get; } = [];

    [ObservableProperty] private CommitNode? _selectedCommit;
    [ObservableProperty] private FileChange? _selectedUnstaged;
    [ObservableProperty] private FileChange? _selectedStaged;
    [ObservableProperty] private FileChange? _selectedConflict;
    [ObservableProperty] private FileChange? _selectedCommitFile;
    [ObservableProperty] private RefTreeNode? _selectedLocalNode;
    [ObservableProperty] private RefTreeNode? _selectedRemoteNode;
    [ObservableProperty] private RefTreeNode? _selectedTagNode;
    [ObservableProperty] private RefTreeNode? _selectedStashNode;
    [ObservableProperty] private RefTreeNode? _selectedWorktreeNode;
    [ObservableProperty] private BranchRef? _selectedLocalBranch;
    [ObservableProperty] private BranchRef? _selectedRemoteBranch;
    [ObservableProperty] private TagRef? _selectedTag;
    [ObservableProperty] private StashEntry? _selectedStash;
    [ObservableProperty] private WorktreeInfo? _selectedWorktree;
    [ObservableProperty] private string? _selectedRecent;

    public bool HasConflictOperation => IsMerging || IsRebasing;

    public MainViewModel()
    {
        foreach (var path in _settings.Load().RecentRepositories)
            RecentRepositories.Add(path);
        HasRecents = RecentRepositories.Count > 0;
    }

    partial void OnErrorMessageChanged(string value) => HasError = !string.IsNullOrWhiteSpace(value);

    partial void OnSelectedUnstagedChanged(FileChange? value)
    {
        if (value is not null && !IsShowingCommitFiles)
            _ = LoadWorktreeDiffAsync(value, DiffKind.WorkTree);
    }

    partial void OnSelectedStagedChanged(FileChange? value)
    {
        if (value is not null && !IsShowingCommitFiles)
            _ = LoadWorktreeDiffAsync(value, DiffKind.Index);
    }

    partial void OnSelectedConflictChanged(FileChange? value)
    {
        if (value is not null)
            _ = LoadConflictFileAsync(value);
    }

    partial void OnSelectedCommitChanged(CommitNode? value)
    {
        IsShowingCommitFiles = value is not null;
        if (value is not null)
            _ = LoadCommitAsync(value);
        else
            _ = ShowWorkingTreeDiffAsync();
    }

    [RelayCommand]
    private void ShowWorkingTree()
    {
        SelectedCommit = null;
        SelectedCommitFile = null;
    }

    partial void OnSelectedCommitFileChanged(FileChange? value)
    {
        if (value is not null && SelectedCommit is not null)
            _ = LoadCommitFileDiffAsync(SelectedCommit, value);
    }

    partial void OnSelectedRecentChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
            _ = OpenRepositoryAsync(value);
    }

    partial void OnSelectedLocalNodeChanged(RefTreeNode? value)
    {
        SelectedLocalBranch = value?.Branch;
        RevealCommit(value?.Branch?.TipSha);
    }

    partial void OnSelectedRemoteNodeChanged(RefTreeNode? value)
    {
        SelectedRemoteBranch = value?.Branch;
        RevealCommit(value?.Branch?.TipSha);
    }

    partial void OnSelectedTagNodeChanged(RefTreeNode? value)
    {
        SelectedTag = value?.Tag;
        RevealCommit(value?.Tag?.TargetSha);
    }

    partial void OnSelectedStashNodeChanged(RefTreeNode? value) =>
        SelectedStash = value?.Stash;

    partial void OnSelectedWorktreeNodeChanged(RefTreeNode? value)
    {
        SelectedWorktree = value?.Worktree;
        RevealCommit(value?.Worktree?.HeadSha);
    }

    public event Action<CommitNode>? CommitRevealed;

    private void RevealCommit(string? sha)
    {
        if (string.IsNullOrWhiteSpace(sha) || Commits.Count == 0)
            return;

        var commit = Commits.FirstOrDefault(c =>
            c.Sha.StartsWith(sha, StringComparison.OrdinalIgnoreCase)
            || sha.StartsWith(c.Sha, StringComparison.OrdinalIgnoreCase));
        if (commit is null)
        {
            StatusMessage = "That commit is not in the loaded graph.";
            return;
        }

        SelectedCommit = commit;
        CommitRevealed?.Invoke(commit);
    }

    partial void OnIsMergingChanged(bool value) => OnPropertyChanged(nameof(HasConflictOperation));
    partial void OnIsRebasingChanged(bool value) => OnPropertyChanged(nameof(HasConflictOperation));

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        if (Host is null)
            return;

        var folders = await Host.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Git repository",
            AllowMultiple = false
        });
        var folder = folders.FirstOrDefault();
        if (folder is null)
            return;

        var path = folder.Path.LocalPath;
        await OpenRepositoryAsync(path);
    }

    public async Task OpenRepositoryAsync(string path)
    {
        var root = GitRepositoryLocator.FindRoot(path);
        if (root is null)
        {
            ErrorMessage = "That folder is not a Git repository.";
            return;
        }

        _watcher?.Dispose();
        _repo?.Dispose();
        _repo = new GitCliRepository(root, new LibGit2HistoryReader());
        _settings.RememberRepository(root);
        if (!RecentRepositories.Contains(root))
            RecentRepositories.Insert(0, root);
        HasRecents = RecentRepositories.Count > 0;
        HasRepo = true;
        RepoPath = root;
        WindowTitle = $"GitTree — {Path.GetFileName(root)}";
        _watcher = new GitRepositoryWatcher(root);
        _watcher.Changed += (_, _) =>
        {
            if (_suppressWatch)
                return;
            Dispatcher.UIThread.Post(() => NeedsRefresh = true);
        };
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_repo is null)
            return;

        var serial = ++_refreshSerial;
        try
        {
            IsBusy = true;
            ErrorMessage = "";
            var snapshot = await _repo.RefreshAsync();
            if (serial != _refreshSerial)
                return;

            ApplySnapshot(snapshot);
            NeedsRefresh = false;
            StatusMessage = $"{snapshot.CurrentBranch}  •  {snapshot.Changes.Count} changed  •  {snapshot.Commits.Count} commits";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            if (serial == _refreshSerial)
                IsBusy = false;
        }
    }

    private void ApplySnapshot(RepositorySnapshot snapshot)
    {
        CurrentBranch = snapshot.CurrentBranch;
        IsMerging = snapshot.Operation.IsMerging;
        IsRebasing = snapshot.Operation.IsRebasing;

        Replace(Commits, snapshot.Commits);
        Replace(Unstaged, snapshot.Changes.Where(c => c.IsUnstaged && !c.IsConflict));
        Replace(Staged, snapshot.Changes.Where(c => c.IsStaged));
        Replace(Conflicts, snapshot.Changes.Where(c => c.IsConflict));
        var local = snapshot.Branches.Where(b => !b.IsRemote).ToList();
        var remote = snapshot.Branches.Where(b => b.IsRemote).ToList();
        ReplaceTree(LocalTree, RefTreeNode.FromBranches(local));
        ReplaceTree(RemoteTree, RefTreeNode.FromBranches(remote));
        ReplaceTree(TagTree, RefTreeNode.FromTags(snapshot.Tags));
        ReplaceTree(StashTree, RefTreeNode.FromStashes(snapshot.Stashes));
        ReplaceTree(WorktreeTree, RefTreeNode.FromWorktrees(snapshot.Worktrees));
        LocalCount = local.Count;
        RemoteCount = remote.Count;
        TagCount = snapshot.Tags.Count;
        StashCount = snapshot.Stashes.Count;
        WorktreeCount = snapshot.Worktrees.Count;
        UnstagedCount = Unstaged.Count;
        StagedCount = Staged.Count;
        ConflictCount = Conflicts.Count;
    }

    [RelayCommand]
    private Task StageAsync() => MutateAsync(r => r.StageAsync(SelectedFilePaths(_unstagedSelection, SelectedUnstaged)));

    [RelayCommand]
    private Task StageAllAsync() => MutateAsync(r => r.StageAsync(Unstaged.Select(f => f.Path)));

    [RelayCommand]
    private Task UnstageAsync() => MutateAsync(r => r.UnstageAsync(SelectedFilePaths(_stagedSelection, SelectedStaged)));

    [RelayCommand]
    private Task UnstageAllAsync() => MutateAsync(r => r.UnstageAsync(Staged.Select(f => f.Path)));

    [RelayCommand]
    private Task DiscardAsync() => MutateAsync(r => r.DiscardAsync(SelectedFilePaths(_unstagedSelection, SelectedUnstaged)));

    [RelayCommand]
    private Task CommitAsync()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage))
        {
            ErrorMessage = "Commit message is required.";
            return Task.CompletedTask;
        }

        var message = CommitMessage;
        return MutateAsync(async r =>
        {
            await r.CommitAsync(message);
            CommitMessage = "";
        });
    }

    [RelayCommand]
    private Task FetchAsync() => MutateAsync(r => r.FetchAsync());

    [RelayCommand]
    private Task PullAsync() => MutateAsync(r => r.PullAsync());

    [RelayCommand]
    private Task PushAsync() => MutateAsync(r => r.PushAsync());

    [RelayCommand]
    private Task CreateBranchAsync()
    {
        if (string.IsNullOrWhiteSpace(NewBranchName))
        {
            ErrorMessage = "Enter a branch name.";
            return Task.CompletedTask;
        }

        var name = NewBranchName.Trim();
        return MutateAsync(async r =>
        {
            await r.CreateBranchAsync(name, SelectedCommit?.Sha);
            NewBranchName = "";
        });
    }

    [RelayCommand]
    private Task CheckoutLocalAsync()
    {
        if (SelectedLocalBranch is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.CheckoutAsync(SelectedLocalBranch.Name));
    }

    [RelayCommand]
    private Task DeleteLocalBranchAsync()
    {
        if (SelectedLocalBranch is null)
        {
            ErrorMessage = "Select a local branch first.";
            return Task.CompletedTask;
        }

        if (SelectedLocalBranch.IsCurrent)
        {
            ErrorMessage = "Switch off this branch before deleting it.";
            return Task.CompletedTask;
        }

        return MutateAsync(r => r.DeleteBranchAsync(SelectedLocalBranch.Name, force: true));
    }

    [RelayCommand]
    private Task CheckoutRemoteAsync()
    {
        if (SelectedRemoteBranch is null)
            return Task.CompletedTask;
        var name = SelectedRemoteBranch.Name;
        var slash = name.IndexOf('/');
        var local = slash >= 0 ? name[(slash + 1)..] : name;
        return MutateAsync(r => r.CreateBranchAsync(local, name));
    }

    [RelayCommand]
    private Task CheckoutCommitAsync()
    {
        if (SelectedCommit is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.CheckoutAsync(SelectedCommit.Sha));
    }

    [RelayCommand]
    private Task CheckoutTagAsync()
    {
        if (SelectedTag is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.CheckoutAsync(SelectedTag.Name));
    }

    [RelayCommand]
    private Task MergeSelectedAsync()
    {
        var name = SelectedLocalBranch?.Name ?? SelectedRemoteBranch?.Name;
        if (name is null)
        {
            ErrorMessage = "Select a branch to merge.";
            return Task.CompletedTask;
        }

        return MutateAsync(r => r.MergeAsync(name));
    }

    [RelayCommand]
    private Task RebaseSelectedAsync()
    {
        var name = SelectedLocalBranch?.Name ?? SelectedRemoteBranch?.Name;
        if (name is null)
        {
            ErrorMessage = "Select a branch to rebase onto.";
            return Task.CompletedTask;
        }

        return MutateAsync(r => r.RebaseAsync(name));
    }

    [RelayCommand]
    private Task StashSaveAsync() => MutateAsync(r => r.StashSaveAsync());

    [RelayCommand]
    private Task StashApplyAsync()
    {
        if (SelectedStash is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.StashApplyAsync(SelectedStash.Index));
    }

    [RelayCommand]
    private Task StashDropAsync()
    {
        if (SelectedStash is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.StashDropAsync(SelectedStash.Index));
    }

    [RelayCommand]
    private async Task SwitchWorktreeAsync()
    {
        if (SelectedWorktree is null || SelectedWorktree.IsCurrent)
            return;
        await OpenRepositoryAsync(SelectedWorktree.Path);
    }

    [RelayCommand]
    private Task ImportWorktreeChangesAsync()
    {
        if (SelectedWorktree is null || SelectedWorktree.IsCurrent)
        {
            ErrorMessage = "Select another worktree to bring changes from.";
            return Task.CompletedTask;
        }

        return MutateAsync(r => r.ImportChangesFromWorktreeAsync(SelectedWorktree));
    }

    [RelayCommand]
    private Task RemoveWorktreeAsync()
    {
        if (SelectedWorktree is null)
        {
            ErrorMessage = "Select a worktree first.";
            return Task.CompletedTask;
        }

        if (!SelectedWorktree.CanRemove)
        {
            ErrorMessage = "The current or main worktree cannot be removed.";
            return Task.CompletedTask;
        }

        return MutateAsync(r => r.RemoveWorktreeAsync(SelectedWorktree));
    }

    [RelayCommand]
    private Task ContinueMergeAsync() => MutateAsync(r => r.ContinueMergeAsync());

    [RelayCommand]
    private Task AbortMergeAsync() => MutateAsync(r => r.AbortMergeAsync());

    [RelayCommand]
    private Task ContinueRebaseAsync() => MutateAsync(r => r.ContinueRebaseAsync());

    [RelayCommand]
    private Task AbortRebaseAsync() => MutateAsync(r => r.AbortRebaseAsync());

    [RelayCommand]
    private Task TakeOursAsync()
    {
        if (SelectedConflict is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.TakeOursAsync(SelectedConflict.Path));
    }

    [RelayCommand]
    private Task TakeTheirsAsync()
    {
        if (SelectedConflict is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.TakeTheirsAsync(SelectedConflict.Path));
    }

    [RelayCommand]
    private Task MarkResolvedAsync()
    {
        if (SelectedConflict is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.MarkResolvedAsync(SelectedConflict.Path));
    }

    [RelayCommand]
    private async Task SaveConflictFileAsync()
    {
        if (_repo is null || SelectedConflict is null)
            return;
        await _repo.WriteWorkingFileAsync(SelectedConflict.Path, ConflictFileText);
        StatusMessage = "Wrote conflict file. Mark resolved when finished editing.";
    }

    private async Task LoadWorktreeDiffAsync(FileChange file, DiffKind kind)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = false;
        DiffHeader = kind == DiffKind.Index ? $"Staged • {file.DisplayPath}" : $"Unstaged • {file.DisplayPath}";
        var text = await _repo.GetDiffAsync(new DiffRequest { Kind = kind, Path = file.Path });
        if (string.IsNullOrWhiteSpace(text) && kind == DiffKind.WorkTree && file.IndexStatus == FileChangeKind.Untracked)
        {
            text = await _repo.ReadWorkingFileAsync(file.Path);
            Replace(DiffLines, DiffLineParser.ParseNewFile(text));
            return;
        }

        Replace(DiffLines, DiffLineParser.Parse(text));
    }

    private async Task LoadConflictFileAsync(FileChange file)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = true;
        DiffHeader = $"Conflict • {file.DisplayPath}";
        ConflictFileText = await _repo.ReadWorkingFileAsync(file.Path);
        Replace(DiffLines, DiffLineParser.Parse(ConflictFileText));
    }

    private async Task LoadCommitAsync(CommitNode commit)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = false;
        var files = await _repo.GetCommitFilesAsync(commit.Sha);
        Replace(CommitFiles, files);
        SelectedCommitFile = files.Count > 0 ? files[0] : null;
        if (SelectedCommitFile is null)
        {
            DiffHeader = $"{commit.ShortSha} • no file changes";
            Replace(DiffLines, []);
        }
    }

    private async Task ShowWorkingTreeDiffAsync()
    {
        Replace(CommitFiles, []);
        if (SelectedConflict is not null)
        {
            await LoadConflictFileAsync(SelectedConflict);
            return;
        }

        if (SelectedUnstaged is not null)
        {
            await LoadWorktreeDiffAsync(SelectedUnstaged, DiffKind.WorkTree);
            return;
        }

        if (SelectedStaged is not null)
        {
            await LoadWorktreeDiffAsync(SelectedStaged, DiffKind.Index);
            return;
        }

        ShowConflictEditor = false;
        DiffHeader = "Working tree";
        Replace(DiffLines, []);
    }

    private async Task LoadCommitFileDiffAsync(CommitNode commit, FileChange file)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = false;
        DiffHeader = $"{commit.ShortSha} • {file.DisplayPath}";
        var text = await _repo.GetDiffAsync(new DiffRequest { Kind = DiffKind.Commit, CommitSha = commit.Sha, Path = file.Path });
        Replace(DiffLines, DiffLineParser.Parse(text));
    }

    private async Task MutateAsync(Func<IGitRepository, Task> action)
    {
        if (_repo is null)
            return;
        _suppressWatch = true;
        var gate = ++_watchGate;
        try
        {
            IsBusy = true;
            ErrorMessage = "";
            await action(_repo);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            try { await RefreshAsync(); } catch { /* keep original error */ }
        }
        finally
        {
            _ = ReleaseWatchSuppressionAsync(gate);
            IsBusy = false;
        }
    }

    private async Task ReleaseWatchSuppressionAsync(int gate)
    {
        await Task.Delay(900);
        if (gate == _watchGate)
            _suppressWatch = false;
    }

    public void SetUnstagedSelection(IEnumerable<FileChange> files) =>
        _unstagedSelection = files.Select(f => f.Path).Distinct().ToList();

    public void SetStagedSelection(IEnumerable<FileChange> files) =>
        _stagedSelection = files.Select(f => f.Path).Distinct().ToList();

    private static IEnumerable<string> SelectedFilePaths(IReadOnlyList<string> selected, FileChange? fallback)
    {
        if (selected.Count > 0)
            return selected;
        return fallback is not null ? [fallback.Path] : [];
    }

    private static void ReplaceTree(ObservableCollection<RefTreeNode> target, IEnumerable<RefTreeNode> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }
}
