namespace GitTree.Core;

public sealed class WorktreeInfo
{
    public required string Path { get; init; }
    public required string HeadSha { get; init; }
    public string? Branch { get; init; }
    public bool IsDetached { get; init; }
    public bool IsBare { get; init; }
    public bool IsLocked { get; init; }
    public bool IsCurrent { get; init; }

    public string FolderName
    {
        get
        {
            var trimmed = Path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            return System.IO.Path.GetFileName(trimmed);
        }
    }

    public string Label
    {
        get
        {
            var place = FolderName;
            if (IsDetached || string.IsNullOrEmpty(Branch))
                return $"{place}  (detached)";
            return $"{place}  ·  {Branch}";
        }
    }

    public string MergeRef => !string.IsNullOrWhiteSpace(Branch) ? Branch : HeadSha;

    public bool CanRemove
    {
        get
        {
            if (IsCurrent || IsBare)
                return false;
            var git = System.IO.Path.Combine(Path, ".git");
            return File.Exists(git);
        }
    }
}
