using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.Core;
using GitTree.Git.Cli;
using GitTree.Git.LibGit2;
using GitTree.App.Views;

namespace GitTree.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly SettingsStore _settings = new();
    private IGitRepository? _repo;
    private GitRepositoryWatcher? _watcher;
    private bool _suppressWatch;
    private int _watchGate;
    private int _refreshSerial;
    private bool _preserveError;
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
    [ObservableProperty] private bool _hasConflicts;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _hasRecents;
    [ObservableProperty] private int _localCount;
    [ObservableProperty] private int _remoteCount;
    [ObservableProperty] private int _tagCount;
    [ObservableProperty] private int _stashCount;
    [ObservableProperty] private int _worktreeCount;
    [ObservableProperty] private bool _isShowingCommitFiles;
    [ObservableProperty] private bool _needsRefresh;
    [ObservableProperty] private int _aheadCount;
    [ObservableProperty] private int _behindCount;
    [ObservableProperty] private bool _hasUpstream;
    [ObservableProperty] private bool _hasAhead;
    [ObservableProperty] private bool _hasBehind;
    [ObservableProperty] private bool _isInSync;
    [ObservableProperty] private bool _hasNoUpstream;
    [ObservableProperty] private string _upstreamName = "";
    [ObservableProperty] private string _aheadBadge = "";
    [ObservableProperty] private string _behindBadge = "";

    private string? _pendingStatus;

    public ObservableCollection<RecentRepoItem> RecentRepositories { get; } = [];
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
    [ObservableProperty] private RecentRepoItem? _selectedRecent;

    public bool HasConflictOperation => IsMerging || IsRebasing || ConflictCount > 0;

    public MainViewModel()
    {
        foreach (var path in _settings.Load().RecentRepositories)
        {
            RecentRepositories.Add(new RecentRepoItem
            {
                Path = path,
                Name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            });
        }

        HasRecents = RecentRepositories.Count > 0;
        _ = LoadRecentDetailsAsync();
    }

    partial void OnErrorMessageChanged(string value) => HasError = !string.IsNullOrWhiteSpace(value);

    partial void OnSelectedUnstagedChanged(FileChange? value)
    {
        if (value is null || IsShowingCommitFiles)
            return;
        if (value.IsConflict)
            _ = LoadConflictFileAsync(value);
        else
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

    partial void OnSelectedRecentChanged(RecentRepoItem? value)
    {
        if (value is null || !Directory.Exists(value.Path))
            return;
        if (string.Equals(RepoPath, value.Path, StringComparison.OrdinalIgnoreCase))
            return;
        _ = OpenRepositoryAsync(value.Path);
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
    partial void OnConflictCountChanged(int value) => OnPropertyChanged(nameof(HasConflictOperation));

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

    [RelayCommand]
    private Task OpenRecentAsync(RecentRepoItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Path))
            return Task.CompletedTask;
        return OpenRepositoryAsync(item.Path);
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (Host is null)
            return;

        var window = new SettingsWindow
        {
            DataContext = new SettingsViewModel()
        };
        await window.ShowDialog(Host);
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
        RememberRecent(root);
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
        _ = LoadRecentDetailsAsync();
    }

    private void RememberRecent(string root)
    {
        var existing = RecentRepositories.FirstOrDefault(r =>
            string.Equals(r.Path, root, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            RecentRepositories.Remove(existing);
        else
        {
            existing = new RecentRepoItem
            {
                Path = root,
                Name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            };
        }

        RecentRepositories.Insert(0, existing);
        while (RecentRepositories.Count > 12)
            RecentRepositories.RemoveAt(RecentRepositories.Count - 1);
    }

    private async Task LoadRecentDetailsAsync()
    {
        var items = RecentRepositories.ToList();
        await Task.WhenAll(items.Select(FillRecentDetailsAsync));
    }

    private static async Task FillRecentDetailsAsync(RecentRepoItem item)
    {
        if (!Directory.Exists(item.Path))
        {
            item.Exists = false;
            item.Branch = "—";
            item.LastCommit = "Folder not found";
            item.LastCommitWhen = "";
            item.StatusLabel = "Missing";
            item.IsDirty = false;
            return;
        }

        try
        {
            var git = new GitCliRunner(item.Path);
            var branchTask = git.RunAsync(["rev-parse", "--abbrev-ref", "HEAD"], throwOnError: false);
            var logTask = git.RunAsync(["log", "-1", "--format=%s%n%ci"], throwOnError: false);
            var statusTask = git.RunAsync(["status", "--porcelain=v1"], throwOnError: false);
            await Task.WhenAll(branchTask, logTask, statusTask);

            var branch = branchTask.Result.Trim();
            item.Exists = true;
            item.Branch = branch is "HEAD" or "" ? "detached" : branch;

            var log = logTask.Result.Replace("\r\n", "\n").Trim();
            var logLines = log.Split('\n', 2);
            item.LastCommit = logLines.Length > 0 ? logLines[0].Trim() : "No commits yet";
            item.LastCommitWhen = logLines.Length > 1 ? FormatRelative(logLines[1].Trim()) : "";

            var dirty = statusTask.Result
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Length;
            item.IsDirty = dirty > 0;
            item.StatusLabel = dirty == 0 ? "Clean" : dirty == 1 ? "1 change" : $"{dirty} changes";
        }
        catch
        {
            item.Branch = "—";
            item.LastCommit = "Could not read Git status";
            item.LastCommitWhen = "";
            item.StatusLabel = "Unavailable";
            item.IsDirty = false;
        }
    }

    private static string FormatRelative(string isoDate)
    {
        if (!DateTimeOffset.TryParse(isoDate, out var when))
            return isoDate;

        var delta = DateTimeOffset.Now - when;
        if (delta.TotalMinutes < 1)
            return "just now";
        if (delta.TotalHours < 1)
            return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalDays < 1)
            return $"{(int)delta.TotalHours}h ago";
        if (delta.TotalDays < 14)
            return $"{(int)delta.TotalDays}d ago";
        return when.ToLocalTime().ToString("MMM dd, yyyy");
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
            if (!_preserveError)
                ErrorMessage = "";
            _preserveError = false;
            var snapshot = await _repo.RefreshAsync();
            if (serial != _refreshSerial)
                return;

            ApplySnapshot(snapshot);
            NeedsRefresh = false;
            StatusMessage = ConsumePendingStatus() ?? DefaultStatus(snapshot);
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
        Replace(Unstaged,
            snapshot.Changes.Where(c => c.IsConflict)
                .Concat(snapshot.Changes.Where(c => c.IsUnstaged && !c.IsConflict)));
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
        HasConflicts = ConflictCount > 0;

        AheadCount = snapshot.Sync.Ahead;
        BehindCount = snapshot.Sync.Behind;
        HasUpstream = snapshot.Sync.HasUpstream;
        HasAhead = snapshot.Sync.HasAhead;
        HasBehind = snapshot.Sync.HasBehind;
        IsInSync = snapshot.Sync.IsInSync;
        HasNoUpstream = !snapshot.IsDetached && !snapshot.Sync.HasUpstream;
        UpstreamName = snapshot.Sync.Upstream ?? "";
        AheadBadge = $"↑ {snapshot.Sync.Ahead}";
        BehindBadge = $"↓ {snapshot.Sync.Behind}";
    }

    [RelayCommand]
    private Task StageAsync() => MutateAsync(r => r.StageAsync(SelectedFilePaths(_unstagedSelection, SelectedUnstaged)));

    [RelayCommand]
    private Task UnstageAsync() => MutateAsync(r => r.UnstageAsync(SelectedFilePaths(_stagedSelection, SelectedStaged)));

    [RelayCommand]
    private Task DiscardAsync() => MutateAsync(r => r.DiscardAsync(SelectedFilePaths(_unstagedSelection, SelectedUnstaged)));

    [RelayCommand]
    private Task CommitAsync()
    {
        if (ConflictCount > 0)
        {
            ErrorMessage = $"Cannot commit: {ConflictCount} file(s) still have conflicts. Resolve them first.";
            StatusMessage = "Finish conflict resolution before committing.";
            return Task.CompletedTask;
        }

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
    private Task FetchAsync() => MutateAsync(async r =>
        _pendingStatus = await r.FetchAsync());

    [RelayCommand]
    private Task PullAsync() => MutateAsync(async r =>
        _pendingStatus = await r.PullAsync());

    [RelayCommand]
    private Task PushAsync() => MutateAsync(async r =>
        _pendingStatus = await r.PushAsync());

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
    private async Task ImportWorktreeChangesAsync()
    {
        if (SelectedWorktree is null || SelectedWorktree.IsCurrent)
        {
            ErrorMessage = "Select another worktree to bring changes from.";
            return;
        }

        if (_repo is null)
            return;

        WorktreeImportPreview preview;
        try
        {
            IsBusy = true;
            ErrorMessage = "";
            preview = await _repo.GetWorktreeImportPreviewAsync(SelectedWorktree);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return;
        }
        finally
        {
            IsBusy = false;
        }

        WorktreeImportSelection? selection = null;
        if (Host is not null)
        {
            var dialogVm = new WorktreeImportViewModel(preview);
            var window = new WorktreeImportWindow { DataContext = dialogVm };
            await window.ShowDialog(Host);
            if (!dialogVm.Confirmed)
                return;
            selection = dialogVm.ToSelection();
            if (selection.IsEmpty)
                return;
        }

        var source = SelectedWorktree;
        _pendingStatus = selection is null || selection.MergeBranch
            ? $"Merged {source.Branch ?? source.FolderName} into the current branch."
            : $"Brought {DescribeImport(selection)} from {source.FolderName}.";
        await MutateAsync(r => r.ImportChangesFromWorktreeAsync(source, selection));
    }

    private static string DescribeImport(WorktreeImportSelection selection)
    {
        var parts = new List<string>();
        if (selection.MergeBranch)
            parts.Add("the branch");
        if (selection.FilePaths.Count == 1)
            parts.Add("1 file");
        else if (selection.FilePaths.Count > 1)
            parts.Add($"{selection.FilePaths.Count} files");
        return parts.Count == 0 ? "nothing" : string.Join(" and ", parts);
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
            _preserveError = true;
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

    private string? ConsumePendingStatus()
    {
        var pending = _pendingStatus;
        _pendingStatus = null;
        if (string.IsNullOrWhiteSpace(pending))
            return null;
        var compact = string.Join("  •  ", pending.Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= 280 ? compact : compact[..277] + "...";
    }

    private string DefaultStatus(RepositorySnapshot snapshot)
    {
        var parts = new List<string> { snapshot.CurrentBranch };
        if (snapshot.Sync.HasAhead)
            parts.Add($"{snapshot.Sync.Ahead} to push");
        if (snapshot.Sync.HasBehind)
            parts.Add($"{snapshot.Sync.Behind} to pull");
        else if (snapshot.Sync.IsInSync)
            parts.Add("in sync");
        else if (!snapshot.Sync.HasUpstream && !snapshot.IsDetached)
            parts.Add("no upstream");
        var conflicts = snapshot.Changes.Count(c => c.IsConflict);
        if (conflicts > 0)
            parts.Add($"{conflicts} conflicts");
        parts.Add($"{snapshot.Changes.Count} changed");
        parts.Add($"{snapshot.Commits.Count} commits");
        return string.Join("  •  ", parts);
    }

    public void SetUnstagedSelection(IEnumerable<FileChange> files) =>
        _unstagedSelection = files.Select(f => f.Path).Distinct().ToList();

    public void SetStagedSelection(IEnumerable<FileChange> files) =>
        _stagedSelection = files.Select(f => f.Path).Distinct().ToList();

    public void OpenWorkingFile(FileChange? file)
    {
        if (file is null || _repo is null)
            return;

        var full = Path.GetFullPath(Path.Combine(_repo.WorkingDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(_repo.WorkingDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "That path is outside the repository.";
            return;
        }

        if (!File.Exists(full))
        {
            ErrorMessage = file.IndexStatus == FileChangeKind.Deleted || file.WorkTreeStatus == FileChangeKind.Deleted
                ? "That file was deleted, so it cannot be opened."
                : "That file is not on disk.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = full,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

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
