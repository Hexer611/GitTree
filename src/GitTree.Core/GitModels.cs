namespace GitTree.Core;

public sealed class BranchRef
{
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public required string TipSha { get; init; }
    public required bool IsRemote { get; init; }
    public required bool IsCurrent { get; init; }
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public string DisplayName => IsRemote ? Name : Name;
}

public sealed class TagRef
{
    public required string Name { get; init; }
    public required string TargetSha { get; init; }
}

public sealed class RemoteInfo
{
    public required string Name { get; init; }
    public required string FetchUrl { get; init; }
}

public sealed class StashEntry
{
    public required int Index { get; init; }
    public required string Selector { get; init; }
    public required string Message { get; init; }
    public required string Sha { get; init; }
}

public sealed class OperationState
{
    public bool IsMerging { get; init; }
    public bool IsRebasing { get; init; }
    public string? ConflictMessage { get; init; }

    public bool HasConflictOperation => IsMerging || IsRebasing;
}

public sealed class RepositorySnapshot
{
    public required string WorkingDirectory { get; init; }
    public required string HeadSha { get; init; }
    public required string CurrentBranch { get; init; }
    public required bool IsDetached { get; init; }
    public required OperationState Operation { get; init; }
    public required IReadOnlyList<FileChange> Changes { get; init; }
    public required IReadOnlyList<CommitNode> Commits { get; init; }
    public required IReadOnlyList<BranchRef> Branches { get; init; }
    public required IReadOnlyList<TagRef> Tags { get; init; }
    public required IReadOnlyList<RemoteInfo> Remotes { get; init; }
    public required IReadOnlyList<StashEntry> Stashes { get; init; }
    public required IReadOnlyList<WorktreeInfo> Worktrees { get; init; }
    public SyncStatus Sync { get; init; } = SyncStatus.None;
}

public enum DiffKind
{
    WorkTree,
    Index,
    Commit,
    Stash
}

public enum DiffPatchAction
{
    Stage,
    Unstage,
    Discard
}

public enum ResetMode
{
    Soft,
    Mixed,
    Hard
}

public sealed class CherryPickOptions
{
    public bool IncludeCommitId { get; init; }
    public bool NoCommit { get; init; }
}

public sealed class DiffRequest
{
    public required DiffKind Kind { get; init; }
    public string? Path { get; init; }
    public string? CommitSha { get; init; }
    public int? StashIndex { get; init; }
}

public interface IGitHistoryReader
{
    IReadOnlyList<CommitNode> ReadCommits(string workingDirectory, int maxCount = 400);
}

public interface IGitRepository : IDisposable
{
    string WorkingDirectory { get; }

    Task<RepositorySnapshot> RefreshAsync(CancellationToken cancellationToken = default);

    Task StageAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    Task UnstageAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    Task DiscardAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default);
    Task ApplyDiffPatchAsync(string patch, DiffPatchAction action, CancellationToken cancellationToken = default);
    Task CommitAsync(string message, bool amend = false, CancellationToken cancellationToken = default);

    Task CheckoutAsync(string refOrSha, CancellationToken cancellationToken = default);
    Task ResetAsync(string sha, ResetMode mode, CancellationToken cancellationToken = default);
    Task CherryPickAsync(string sha, CherryPickOptions? options = null, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(string name, string? startPoint = null, CancellationToken cancellationToken = default);
    Task DeleteBranchAsync(string name, bool force = false, CancellationToken cancellationToken = default);

    Task<string> FetchAsync(string? remote = null, CancellationToken cancellationToken = default);
    Task<string> PullAsync(CancellationToken cancellationToken = default);
    Task<string> PushAsync(string? remote = null, string? branch = null, CancellationToken cancellationToken = default);

    Task StashSaveAsync(string? message = null, IReadOnlyList<string>? paths = null, CancellationToken cancellationToken = default);
    Task StashApplyAsync(int index, IReadOnlyList<string>? paths = null, CancellationToken cancellationToken = default);
    Task StashDropAsync(int index, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileChange>> GetStashFilesAsync(int index, CancellationToken cancellationToken = default);

    Task<WorktreeImportPreview> GetWorktreeImportPreviewAsync(WorktreeInfo worktree, CancellationToken cancellationToken = default);
    Task ImportChangesFromWorktreeAsync(WorktreeInfo worktree, WorktreeImportSelection? selection = null, CancellationToken cancellationToken = default);
    Task RemoveWorktreeAsync(WorktreeInfo worktree, CancellationToken cancellationToken = default);

    Task MergeAsync(string branch, CancellationToken cancellationToken = default);
    Task RebaseAsync(string onto, CancellationToken cancellationToken = default);
    Task ContinueMergeAsync(CancellationToken cancellationToken = default);
    Task AbortMergeAsync(CancellationToken cancellationToken = default);
    Task ContinueRebaseAsync(CancellationToken cancellationToken = default);
    Task AbortRebaseAsync(CancellationToken cancellationToken = default);

    Task TakeOursAsync(string path, CancellationToken cancellationToken = default);
    Task TakeTheirsAsync(string path, CancellationToken cancellationToken = default);
    Task MarkResolvedAsync(string path, CancellationToken cancellationToken = default);

    Task<string> GetDiffAsync(DiffRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(string sha, CancellationToken cancellationToken = default);
    Task<string> ReadWorkingFileAsync(string path, CancellationToken cancellationToken = default);
    Task WriteWorkingFileAsync(string path, string content, CancellationToken cancellationToken = default);
}
