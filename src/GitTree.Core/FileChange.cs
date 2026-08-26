namespace GitTree.Core;

public enum FileChangeKind
{
    Unmodified,
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Ignored,
    TypeChange,
    Unmerged
}

public sealed class FileChange
{
    public required string Path { get; init; }
    public string? OldPath { get; init; }
    public FileChangeKind IndexStatus { get; init; }
    public FileChangeKind WorkTreeStatus { get; init; }
    public bool IsConflict { get; init; }
    public bool IsStaged => IndexStatus is not FileChangeKind.Unmodified and not FileChangeKind.Untracked and not FileChangeKind.Unmerged
                            && !IsConflict;
    public bool IsUnstaged => WorkTreeStatus is not FileChangeKind.Unmodified || IndexStatus == FileChangeKind.Untracked;
    public string DisplayPath => OldPath is null ? Path : $"{OldPath} → {Path}";

    public string StatusLabel
    {
        get
        {
            if (IsConflict)
                return "Conflict";
            if (IndexStatus == FileChangeKind.Untracked)
                return "Untracked";
            if (IndexStatus != FileChangeKind.Unmodified && WorkTreeStatus != FileChangeKind.Unmodified)
                return $"Staged {IndexStatus} / Unstaged {WorkTreeStatus}";
            if (IndexStatus != FileChangeKind.Unmodified)
                return $"Staged {IndexStatus}";
            return WorkTreeStatus.ToString();
        }
    }

    public string BadgeChar
    {
        get
        {
            if (IsConflict)
                return "C";
            if (IndexStatus == FileChangeKind.Untracked)
                return "?";
            var kind = IndexStatus != FileChangeKind.Unmodified ? IndexStatus : WorkTreeStatus;
            return kind switch
            {
                FileChangeKind.Added => "A",
                FileChangeKind.Deleted => "D",
                FileChangeKind.Renamed => "R",
                FileChangeKind.Copied => "P",
                FileChangeKind.Untracked => "?",
                _ => "M"
            };
        }
    }
}
