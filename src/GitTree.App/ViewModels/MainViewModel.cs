using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
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
    private List<DiffLine> _diffLineSelection = [];
    private IReadOnlyList<BranchRef> _branches = [];

    public Window? Host { get; set; }

    [ObservableProperty] private string _windowTitle = "GitTree";
    [ObservableProperty] private string _repoPath = "No repository open";
    [ObservableProperty] private string _currentBranch = "";
    [ObservableProperty] private bool _isDetached;
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
    [ObservableProperty] private int _removedLineCount;
    [ObservableProperty] private int _addedLineCount;
    [ObservableProperty] private int _selectedDiffChangeCount;
    [ObservableProperty] private DiffKind? _activeDiffKind;
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
    [ObservableProperty] private string _revisionFilesHeading = "CHANGED IN COMMIT";
    [ObservableProperty] private string _revisionFilesSha = "";
    [ObservableProperty] private string _revisionFilesSubject = "";
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
    [ObservableProperty] private bool _localSectionExpanded = true;
    [ObservableProperty] private bool _remotesSectionExpanded = true;
    [ObservableProperty] private bool _tagsSectionExpanded;
    [ObservableProperty] private bool _stashesSectionExpanded;
    [ObservableProperty] private bool _worktreesSectionExpanded = true;

    private string? _pendingStatus;
    private bool _suppressRecentSelect;

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
    public bool HasDiffLineStats => RemovedLineCount > 0 || AddedLineCount > 0;
    public bool HasRemovedLineStats => RemovedLineCount > 0;
    public bool HasAddedLineStats => AddedLineCount > 0;
    public bool HasSelectedDiffLines => SelectedDiffChangeCount > 0;
    public bool CanStageSelectedDiffLines => HasSelectedDiffLines && ActiveDiffKind == DiffKind.WorkTree && !IsShowingCommitFiles && !ShowConflictEditor;
    public bool CanDiscardSelectedDiffLines => CanStageSelectedDiffLines;
    public bool CanUnstageSelectedDiffLines => HasSelectedDiffLines && ActiveDiffKind == DiffKind.Index && !IsShowingCommitFiles && !ShowConflictEditor;
    public string SelectedDiffChangeCountText => SelectedDiffChangeCount == 1 ? "1 line" : $"{SelectedDiffChangeCount} lines";

    public MainViewModel()
    {
        var layout = _settings.Load();
        _localSectionExpanded = layout.SidebarLocalExpanded;
        _remotesSectionExpanded = layout.SidebarRemotesExpanded;
        _tagsSectionExpanded = layout.SidebarTagsExpanded;
        _stashesSectionExpanded = layout.SidebarStashesExpanded;
        _worktreesSectionExpanded = layout.SidebarWorktreesExpanded;

        var projects = _settings.LoadRecentProjects();
        foreach (var path in projects)
        {
            RecentRepositories.Add(new RecentRepoItem
            {
                Path = path,
                Name = GitRepositoryLocator.FolderName(path)
            });
        }

        HasRecents = RecentRepositories.Count > 0;
        _ = LoadRecentDetailsAsync();
    }

    partial void OnErrorMessageChanged(string value) => HasError = !string.IsNullOrWhiteSpace(value);

    partial void OnLocalSectionExpandedChanged(bool value) => PersistSidebarLayout();
    partial void OnRemotesSectionExpandedChanged(bool value) => PersistSidebarLayout();
    partial void OnTagsSectionExpandedChanged(bool value) => PersistSidebarLayout();
    partial void OnStashesSectionExpandedChanged(bool value) => PersistSidebarLayout();
    partial void OnWorktreesSectionExpandedChanged(bool value) => PersistSidebarLayout();

    private void PersistSidebarLayout()
    {
        _settings.Update(s =>
        {
            s.SidebarLocalExpanded = LocalSectionExpanded;
            s.SidebarRemotesExpanded = RemotesSectionExpanded;
            s.SidebarTagsExpanded = TagsSectionExpanded;
            s.SidebarStashesExpanded = StashesSectionExpanded;
            s.SidebarWorktreesExpanded = WorktreesSectionExpanded;
        });
    }

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
        if (value is not null)
        {
            if (SelectedStashNode is not null)
                SelectedStashNode = null;
            IsShowingCommitFiles = true;
            _ = LoadCommitAsync(value);
            return;
        }

        if (SelectedStash is null)
        {
            IsShowingCommitFiles = false;
            _ = ShowWorkingTreeDiffAsync();
        }
    }

    [RelayCommand]
    private void ShowWorkingTree()
    {
        SelectedCommit = null;
        SelectedStashNode = null;
        SelectedCommitFile = null;
    }

    partial void OnSelectedCommitFileChanged(FileChange? value)
    {
        if (value is null)
            return;
        if (SelectedStash is not null)
            _ = LoadStashFileDiffAsync(SelectedStash, value);
        else if (SelectedCommit is not null)
            _ = LoadCommitFileDiffAsync(SelectedCommit, value);
    }

    partial void OnSelectedRecentChanged(RecentRepoItem? value)
    {
        if (_suppressRecentSelect || value is null || !Directory.Exists(value.Path))
            return;
        if (HasRepo && GitRepositoryLocator.IsSameProject(RepoPath, value.Path))
            return;

        // ComboBox selection must finish before we touch the recents list.
        var path = value.Path;
        Dispatcher.UIThread.Post(() => _ = OpenRepositoryAsync(path, bumpRecent: false));
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

    partial void OnSelectedStashNodeChanged(RefTreeNode? value)
    {
        SelectedStash = value?.Stash;
        if (SelectedStash is not null)
        {
            if (SelectedCommit is not null)
                SelectedCommit = null;
            IsShowingCommitFiles = true;
            _ = LoadStashAsync(SelectedStash);
            return;
        }

        if (SelectedCommit is null)
        {
            IsShowingCommitFiles = false;
            _ = ShowWorkingTreeDiffAsync();
        }
    }

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
    partial void OnRemovedLineCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiffLineStats));
        OnPropertyChanged(nameof(HasRemovedLineStats));
        OnPropertyChanged(nameof(RemovedLineCountText));
    }

    partial void OnAddedLineCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDiffLineStats));
        OnPropertyChanged(nameof(HasAddedLineStats));
        OnPropertyChanged(nameof(AddedLineCountText));
    }

    partial void OnSelectedDiffChangeCountChanged(int value) => NotifyDiffLineCommands();
    partial void OnActiveDiffKindChanged(DiffKind? value) => NotifyDiffLineCommands();
    partial void OnIsShowingCommitFilesChanged(bool value) => NotifyDiffLineCommands();
    partial void OnShowConflictEditorChanged(bool value) => NotifyDiffLineCommands();

    private void NotifyDiffLineCommands()
    {
        OnPropertyChanged(nameof(HasSelectedDiffLines));
        OnPropertyChanged(nameof(CanStageSelectedDiffLines));
        OnPropertyChanged(nameof(CanDiscardSelectedDiffLines));
        OnPropertyChanged(nameof(CanUnstageSelectedDiffLines));
        OnPropertyChanged(nameof(SelectedDiffChangeCountText));
        StageSelectedLinesCommand.NotifyCanExecuteChanged();
        DiscardSelectedLinesCommand.NotifyCanExecuteChanged();
        UnstageSelectedLinesCommand.NotifyCanExecuteChanged();
    }

    public string RemovedLineCountText => $"−{RemovedLineCount}";
    public string AddedLineCountText => $"+{AddedLineCount}";

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

    public Task OpenRepositoryAsync(string path) => OpenRepositoryAsync(path, bumpRecent: true);

    public async Task OpenRepositoryAsync(string path, bool bumpRecent)
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
        RememberRecent(root, bumpRecent);
        HasRecents = RecentRepositories.Count > 0;
        HasRepo = true;
        RepoPath = root;
        WindowTitle = $"GitTree — {GitRepositoryLocator.FolderName(root)}";
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

    private void RememberRecent(string openedRoot, bool bump)
    {
        var projectRoot = GitRepositoryLocator.ResolveProjectForRecents(
            openedRoot, RecentRepositories.Select(r => r.Path));

        _suppressRecentSelect = true;
        try
        {
            DropWorktreesFromRecents();

            if (projectRoot is null)
            {
                var current = RecentRepositories.FirstOrDefault(r =>
                    GitRepositoryLocator.IsSameProject(r.Path, openedRoot));
                if (current is not null)
                    SyncSelectedRecent(current);
                return;
            }

            var existing = RecentRepositories.FirstOrDefault(r =>
                GitRepositoryLocator.PathsEqual(r.Path, projectRoot)
                || GitRepositoryLocator.IsSameProject(r.Path, projectRoot));

            if (existing is null)
            {
                existing = new RecentRepoItem
                {
                    Path = projectRoot,
                    Name = GitRepositoryLocator.FolderName(projectRoot)
                };
                RecentRepositories.Insert(0, existing);
            }
            else
            {
                existing.Path = projectRoot;
                existing.Name = GitRepositoryLocator.FolderName(projectRoot);
                var index = RecentRepositories.IndexOf(existing);
                if (bump && index > 0)
                    RecentRepositories.Move(index, 0);
            }

            while (RecentRepositories.Count > 12)
                RecentRepositories.RemoveAt(RecentRepositories.Count - 1);

            SyncSelectedRecent(existing);
        }
        finally
        {
            var selected = SelectedRecent;
            Dispatcher.UIThread.Post(() =>
            {
                _suppressRecentSelect = true;
                try
                {
                    if (selected is not null)
                        SyncSelectedRecent(selected);
                }
                finally
                {
                    _suppressRecentSelect = false;
                }
            });
        }
    }

    private void DropWorktreesFromRecents()
    {
        for (var i = RecentRepositories.Count - 1; i >= 0; i--)
        {
            if (GitRepositoryLocator.IsAuxiliaryWorktreePath(RecentRepositories[i].Path))
                RecentRepositories.RemoveAt(i);
        }
    }

    private void SyncSelectedRecent(RecentRepoItem item)
    {
        // ComboBox tracks SelectedIndex. Re-assign so it re-resolves after the list moves.
        SelectedRecent = null;
        SelectedRecent = item;
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

            item.Exists = true;
            var headState = HeadRefParser.Resolve(
                HeadRefParser.TryRead(GitDir.Resolve(item.Path)), branchTask.Result, null, null);
            item.Branch = headState.IsDetached ? "detached" : headState.CurrentBranch;

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
        IsDetached = snapshot.IsDetached;
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
        _branches = snapshot.Branches;
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

    [RelayCommand(CanExecute = nameof(CanStageSelectedDiffLines))]
    private Task StageSelectedLinesAsync() => ApplySelectedDiffLinesAsync(DiffPatchAction.Stage);

    [RelayCommand(CanExecute = nameof(CanUnstageSelectedDiffLines))]
    private Task UnstageSelectedLinesAsync() => ApplySelectedDiffLinesAsync(DiffPatchAction.Unstage);

    [RelayCommand(CanExecute = nameof(CanDiscardSelectedDiffLines))]
    private Task DiscardSelectedLinesAsync() => ApplySelectedDiffLinesAsync(DiffPatchAction.Discard);

    private async Task ApplySelectedDiffLinesAsync(DiffPatchAction action)
    {
        var file = action == DiffPatchAction.Unstage ? SelectedStaged : SelectedUnstaged;
        if (file is null || _diffLineSelection.Count == 0)
            return;

        var selected = _diffLineSelection.ToList();
        var diff = DiffLines.ToList();
        var path = file.Path;
        var isUntracked = file.IndexStatus == FileChangeKind.Untracked;
        var allChanges = diff.Where(l => l.IsChange).ToList();
        var allSelected = allChanges.Count > 0 && allChanges.All(c => _diffLineSelection.Exists(s => ReferenceEquals(s, c)));

        await MutateAsync(async r =>
        {
            if (allSelected)
            {
                if (action == DiffPatchAction.Stage)
                    await r.StageAsync([path]);
                else if (action == DiffPatchAction.Unstage)
                    await r.UnstageAsync([path]);
                else
                    await r.DiscardAsync([path]);
                return;
            }

            if (action == DiffPatchAction.Discard && isUntracked)
            {
                var remaining = SelectedDiffPatch.KeepUnselectedNewFileContent(diff, selected);
                if (string.IsNullOrEmpty(remaining))
                    await r.DiscardAsync([path]);
                else
                    await r.WriteWorkingFileAsync(path, remaining);
                return;
            }

            var mode = action == DiffPatchAction.Stage
                ? SelectedDiffPatchMode.MatchOld
                : SelectedDiffPatchMode.MatchNew;
            var patch = SelectedDiffPatch.Build(
                diff,
                selected,
                path,
                mode,
                isNewFile: isUntracked,
                isDeletedFile: file.WorkTreeStatus == FileChangeKind.Deleted || file.IndexStatus == FileChangeKind.Deleted);
            if (string.IsNullOrWhiteSpace(patch))
                throw new InvalidOperationException("Those lines cannot be applied as a patch.");
            await r.ApplyDiffPatchAsync(patch, action);
        });

        RestoreFileAfterLineEdit(action, path);
    }

    private void RestoreFileAfterLineEdit(DiffPatchAction action, string path)
    {
        if (action == DiffPatchAction.Unstage)
        {
            SelectedStaged = Staged.FirstOrDefault(f => f.Path == path);
            if (SelectedStaged is not null)
                _ = LoadWorktreeDiffAsync(SelectedStaged, DiffKind.Index);
            else if (SelectedUnstaged is null)
                _ = ShowWorkingTreeDiffAsync();
            return;
        }

        SelectedUnstaged = Unstaged.FirstOrDefault(f => f.Path == path);
        if (SelectedUnstaged is not null)
            _ = LoadWorktreeDiffAsync(SelectedUnstaged, DiffKind.WorkTree);
        else
            _ = ShowWorkingTreeDiffAsync();
    }

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
    private async Task CheckoutCommitAsync()
    {
        if (SelectedCommit is null || Host is null)
            return;

        var choices = CheckoutChoicesFor(SelectedCommit);
        var dialogVm = new CheckoutCommitViewModel(SelectedCommit, choices);
        var window = new CheckoutCommitWindow { DataContext = dialogVm };
        await window.ShowDialog(Host);
        if (!dialogVm.Confirmed || dialogVm.SelectedChoice is null)
            return;

        await CheckoutChoiceAsync(dialogVm.SelectedChoice);
    }

    [RelayCommand]
    private Task CheckoutChoiceAsync(CheckoutChoice? choice)
    {
        if (choice is null || string.IsNullOrWhiteSpace(choice.RefOrSha))
            return Task.CompletedTask;

        if (choice.IsDetached)
        {
            _pendingStatus = $"Checked out {ShortSha(choice.RefOrSha)} (detached).";
            return MutateAsync(r => r.CheckoutAsync(choice.RefOrSha));
        }

        if (choice.IsRemote)
        {
            var local = choice.LocalName;
            var localExists = _branches.Any(b =>
                !b.IsRemote && b.Name.Equals(local, StringComparison.OrdinalIgnoreCase));
            if (localExists)
            {
                _pendingStatus = $"Checked out {choice.RefOrSha} (detached; local '{local}' already exists).";
                return MutateAsync(r => r.CheckoutAsync(choice.RefOrSha));
            }

            _pendingStatus = $"Checked out {local} from {choice.RefOrSha}.";
            return MutateAsync(r => r.CreateBranchAsync(local, choice.RefOrSha));
        }

        _pendingStatus = $"Checked out {choice.RefOrSha}.";
        return MutateAsync(r => r.CheckoutAsync(choice.RefOrSha));
    }

    public IReadOnlyList<CheckoutChoice> CheckoutChoicesFor(CommitNode? commit) =>
        CheckoutTargets.ForCommit(commit?.Sha, _branches);

    private static string ShortSha(string sha) => sha.Length >= 7 ? sha[..7] : sha;

    [RelayCommand]
    private async Task ResetToCommitAsync()
    {
        if (SelectedCommit is null)
            return;

        if (IsDetached)
        {
            ErrorMessage = "Checkout a branch before resetting. Reset moves the current branch pointer.";
            return;
        }

        if (Host is null)
            return;

        var dialogVm = new ResetCommitViewModel(CurrentBranch, SelectedCommit);
        var window = new ResetCommitWindow { DataContext = dialogVm };
        await window.ShowDialog(Host);
        if (!dialogVm.Confirmed)
            return;

        var commit = SelectedCommit;
        var mode = dialogVm.SelectedMode.Mode;
        _pendingStatus = $"Reset {CurrentBranch} to {commit.ShortSha} ({mode.ToString().ToLowerInvariant()}).";
        await MutateAsync(r => r.ResetAsync(commit.Sha, mode));
        if (string.IsNullOrWhiteSpace(ErrorMessage))
            RevealCommit(commit.Sha);
    }

    [RelayCommand]
    private Task CherryPickCommitAsync() =>
        CherryPickSelectedAsync(includeCommitId: false, noCommit: false);

    [RelayCommand]
    private Task CherryPickCommitWithIdAsync() =>
        CherryPickSelectedAsync(includeCommitId: true, noCommit: false);

    [RelayCommand]
    private Task CherryPickCommitNoCommitAsync() =>
        CherryPickSelectedAsync(includeCommitId: false, noCommit: true);

    private Task CherryPickSelectedAsync(bool includeCommitId, bool noCommit)
    {
        if (SelectedCommit is null)
            return Task.CompletedTask;

        var commit = SelectedCommit;
        var options = new CherryPickOptions
        {
            IncludeCommitId = includeCommitId,
            NoCommit = noCommit
        };
        _pendingStatus = noCommit
            ? $"Applied {commit.ShortSha} without committing."
            : includeCommitId
                ? $"Cherry-picked {commit.ShortSha} (recorded origin)."
                : $"Cherry-picked {commit.ShortSha}.";
        return MutateAsync(r => r.CherryPickAsync(commit.Sha, options));
    }

    [RelayCommand]
    private Task CheckoutTagAsync()
    {
        if (SelectedTag is null)
            return Task.CompletedTask;
        return MutateAsync(r => r.CheckoutAsync(SelectedTag.Name));
    }

    [RelayCommand]
    private Task MergeCommitAsync()
    {
        if (SelectedCommit is null)
            return Task.CompletedTask;

        var commit = SelectedCommit;
        _pendingStatus = string.IsNullOrWhiteSpace(CurrentBranch) || IsDetached
            ? $"Merged {commit.ShortSha}."
            : $"Merged {commit.ShortSha} into {CurrentBranch}.";
        return MutateAsync(r => r.MergeAsync(commit.Sha));
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
    private async Task StashSaveAsync()
    {
        var files = CurrentWorkingFiles();
        if (files.Count == 0)
        {
            ErrorMessage = "Nothing to stash.";
            return;
        }

        var pick = await ShowStashPickAsync(
            "Stash changes",
            "Choose a name and the files to stash. Unselected files stay in the working tree.",
            "Stash",
            files,
            showMessage: true);
        if (pick is null)
            return;

        var name = string.IsNullOrWhiteSpace(pick.Message) ? null : pick.Message.Trim();
        _pendingStatus = name is null
            ? $"Stashed {DescribeFiles(pick.SelectedPaths.Count)}."
            : $"Stashed {DescribeFiles(pick.SelectedPaths.Count)} as “{name}”.";
        await MutateAsync(r => r.StashSaveAsync(name, pick.SelectedPaths));
    }

    [RelayCommand]
    private async Task StashApplyAsync()
    {
        if (SelectedStash is null || _repo is null)
            return;

        IReadOnlyList<FileChange> files;
        try
        {
            IsBusy = true;
            ErrorMessage = "";
            files = await _repo.GetStashFilesAsync(SelectedStash.Index);
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

        var pick = await ShowStashPickAsync(
            "Get stash changes",
            "Select the files to bring back from this stash. The stash itself is kept.",
            "Get changes",
            files,
            showMessage: false);
        if (pick is null)
            return;

        var stash = SelectedStash;
        _pendingStatus = $"Brought {DescribeFiles(pick.SelectedPaths.Count)} from {stash.Selector}.";
        await MutateAsync(r => r.StashApplyAsync(stash.Index, pick.SelectedPaths));
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
        await OpenRepositoryAsync(SelectedWorktree.Path, bumpRecent: false);
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
        ActiveDiffKind = kind;
        DiffHeader = kind == DiffKind.Index ? $"Staged • {file.DisplayPath}" : $"Unstaged • {file.DisplayPath}";
        var text = await _repo.GetDiffAsync(new DiffRequest { Kind = kind, Path = file.Path });
        if (string.IsNullOrWhiteSpace(text) && kind == DiffKind.WorkTree && file.IndexStatus == FileChangeKind.Untracked)
        {
            text = await _repo.ReadWorkingFileAsync(file.Path);
            SetDiffLines(DiffLineParser.ParseNewFile(text));
            return;
        }

        SetDiffLines(DiffLineParser.Parse(text));
    }

    private async Task LoadConflictFileAsync(FileChange file)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = true;
        ActiveDiffKind = null;
        DiffHeader = $"Conflict • {file.DisplayPath}";
        ConflictFileText = await _repo.ReadWorkingFileAsync(file.Path);
        SetDiffLines(DiffLineParser.Parse(ConflictFileText));
    }

    private async Task LoadCommitAsync(CommitNode commit)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = false;
        SetRevisionHeader("CHANGED IN COMMIT", commit.ShortSha, commit.Subject);
        var files = await _repo.GetCommitFilesAsync(commit.Sha);
        Replace(CommitFiles, files);
        SelectedCommitFile = files.Count > 0 ? files[0] : null;
        if (SelectedCommitFile is null)
        {
            DiffHeader = $"{commit.ShortSha} • no file changes";
            ActiveDiffKind = DiffKind.Commit;
            SetDiffLines([]);
        }
    }

    private async Task LoadStashAsync(StashEntry stash)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = false;
        SetRevisionHeader("CHANGED IN STASH", stash.DisplayLabel, stash.Message);
        var files = await _repo.GetStashFilesAsync(stash.Index);
        Replace(CommitFiles, files);
        SelectedCommitFile = files.Count > 0 ? files[0] : null;
        if (SelectedCommitFile is null)
        {
            DiffHeader = $"{stash.Selector} • no file changes";
            ActiveDiffKind = DiffKind.Stash;
            SetDiffLines([]);
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
        ActiveDiffKind = null;
        SetDiffLines([]);
    }

    private async Task LoadCommitFileDiffAsync(CommitNode commit, FileChange file)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = false;
        ActiveDiffKind = DiffKind.Commit;
        DiffHeader = $"{commit.ShortSha} • {file.DisplayPath}";
        var text = await _repo.GetDiffAsync(new DiffRequest { Kind = DiffKind.Commit, CommitSha = commit.Sha, Path = file.Path });
        SetDiffLines(DiffLineParser.Parse(text));
    }

    private async Task LoadStashFileDiffAsync(StashEntry stash, FileChange file)
    {
        if (_repo is null)
            return;
        ShowConflictEditor = false;
        ActiveDiffKind = DiffKind.Stash;
        DiffHeader = $"{stash.Selector} • {file.DisplayPath}";
        var text = await _repo.GetDiffAsync(new DiffRequest { Kind = DiffKind.Stash, StashIndex = stash.Index, Path = file.Path });
        if (string.IsNullOrWhiteSpace(text) && file.IndexStatus == FileChangeKind.Untracked)
        {
            SetDiffLines([]);
            return;
        }

        SetDiffLines(DiffLineParser.Parse(text));
    }

    private void SetRevisionHeader(string heading, string sha, string subject)
    {
        RevisionFilesHeading = heading;
        RevisionFilesSha = sha;
        RevisionFilesSubject = subject;
    }

    private IReadOnlyList<FileChange> CurrentWorkingFiles() =>
        Unstaged.Concat(Staged)
            .GroupBy(f => f.Path, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

    private async Task<StashPickViewModel?> ShowStashPickAsync(
        string title,
        string subtitle,
        string confirmText,
        IReadOnlyList<FileChange> files,
        bool showMessage)
    {
        var dialogVm = new StashPickViewModel(title, subtitle, confirmText, files, showMessage);
        if (Host is null)
            return dialogVm.CanConfirm ? dialogVm : null;

        var window = new StashPickWindow { DataContext = dialogVm };
        await window.ShowDialog(Host);
        return dialogVm.Confirmed && dialogVm.CanConfirm ? dialogVm : null;
    }

    private static string DescribeFiles(int count) =>
        count == 1 ? "1 file" : $"{count} files";

    private void SetDiffLines(IEnumerable<DiffLine> lines)
    {
        var list = lines as IList<DiffLine> ?? lines.ToList();
        Replace(DiffLines, list);
        RemovedLineCount = list.Count(l => l.Kind == DiffLineKind.Removed);
        AddedLineCount = list.Count(l => l.Kind == DiffLineKind.Added);
        SetDiffLineSelection([]);
    }

    public IReadOnlyList<DiffLine> SetDiffLineSelection(IEnumerable<DiffLine> lines)
    {
        var expanded = SelectedDiffPatch.ExpandSelection(DiffLines, lines).ToList();
        _diffLineSelection = expanded;
        SelectedDiffChangeCount = expanded.Count;
        return expanded;
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
