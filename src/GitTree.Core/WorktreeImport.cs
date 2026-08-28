namespace GitTree.Core;

public sealed class WorktreeImportCommit
{
    public required string Sha { get; init; }
    public required string Subject { get; init; }
    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;
}

public sealed class WorktreeImportPreview
{
    public required WorktreeInfo Worktree { get; init; }
    public required IReadOnlyList<WorktreeImportCommit> Commits { get; init; }
    public required IReadOnlyList<FileChange> Files { get; init; }
    public bool IsEmpty => Commits.Count == 0 && Files.Count == 0;
}

public sealed class WorktreeImportSelection
{
    public bool MergeBranch { get; init; }
    public IReadOnlyList<string> FilePaths { get; init; } = [];
    public bool IsEmpty => !MergeBranch && FilePaths.Count == 0;
}
