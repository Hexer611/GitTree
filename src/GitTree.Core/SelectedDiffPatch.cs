namespace GitTree.Core;

public enum SelectedDiffPatchMode
{
    /// <summary>Old side matches the file being patched (index for unstaged). Used to stage lines.</summary>
    MatchOld,
    /// <summary>New side matches the file being reverse-patched (worktree or index). Used to discard or unstage lines.</summary>
    MatchNew
}

public static class SelectedDiffPatch
{
    public static string? Build(
        IReadOnlyList<DiffLine> lines,
        IReadOnlyCollection<DiffLine> selected,
        string path,
        SelectedDiffPatchMode mode,
        bool isNewFile = false,
        bool isDeletedFile = false)
    {
        var chosen = new HashSet<DiffLine>(selected, DiffLineRefComparer.Instance);
        if (chosen.Count == 0)
            return null;

        var hunks = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != DiffLineKind.Hunk)
                continue;

            var oldStart = lines[i].HunkOldStart ?? 1;
            var newStart = lines[i].HunkNewStart ?? 1;
            var body = new List<(char Prefix, string Text)>();
            i++;
            while (i < lines.Count && lines[i].Kind != DiffLineKind.Hunk)
            {
                var line = lines[i];
                var include = Transform(line, chosen.Contains(line), mode);
                if (include is { } row)
                    body.Add(row);
                i++;
            }

            i--;
            if (body.All(r => r.Prefix == ' '))
                continue;

            var oldCount = body.Count(r => r.Prefix is ' ' or '-');
            var newCount = body.Count(r => r.Prefix is ' ' or '+');
            hunks.Add($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");
            foreach (var (prefix, text) in body)
                hunks.Add($"{prefix}{text}");
        }

        if (hunks.Count == 0)
            return null;

        var gitPath = path.Replace('\\', '/');
        var header = new List<string> { $"diff --git a/{gitPath} b/{gitPath}" };
        if (isNewFile && mode == SelectedDiffPatchMode.MatchOld)
        {
            header.Add("new file mode 100644");
            header.Add("--- /dev/null");
            header.Add($"+++ b/{gitPath}");
        }
        else if (isDeletedFile && mode == SelectedDiffPatchMode.MatchOld && hunks.TrueForAll(l => !l.StartsWith('+')))
        {
            header.Add("deleted file mode 100644");
            header.Add($"--- a/{gitPath}");
            header.Add("+++ /dev/null");
        }
        else
        {
            header.Add($"--- a/{gitPath}");
            header.Add($"+++ b/{gitPath}");
        }

        return string.Join('\n', header.Concat(hunks)) + "\n";
    }

    public static string KeepUnselectedNewFileContent(
        IReadOnlyList<DiffLine> lines,
        IReadOnlyCollection<DiffLine> selected)
    {
        var skip = new HashSet<DiffLine>(selected, DiffLineRefComparer.Instance);
        var kept = lines
            .Where(l => l.Kind == DiffLineKind.Added && !skip.Contains(l))
            .Select(l => l.DisplayText)
            .ToList();
        if (kept.Count == 0)
            return "";
        return string.Join('\n', kept) + "\n";
    }

    public static IReadOnlyList<DiffLine> ExpandSelection(
        IReadOnlyList<DiffLine> all,
        IEnumerable<DiffLine> selected)
    {
        var result = new List<DiffLine>();
        var seen = new HashSet<DiffLine>();
        var selectedList = selected.ToList();
        foreach (var line in selectedList)
        {
            if (line.Kind == DiffLineKind.Hunk)
            {
                foreach (var change in ChangesInHunk(all, line))
                    Add(change);
                continue;
            }

            if (line.IsChange)
                Add(line);
        }

        return result;

        void Add(DiffLine line)
        {
            if (seen.Add(line))
                result.Add(line);
        }
    }

    public static IEnumerable<DiffLine> ChangesInHunk(IReadOnlyList<DiffLine> all, DiffLine hunk)
    {
        var start = -1;
        for (var i = 0; i < all.Count; i++)
        {
            if (ReferenceEquals(all[i], hunk))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
            yield break;

        for (var i = start; i < all.Count; i++)
        {
            if (all[i].Kind == DiffLineKind.Hunk)
                yield break;
            if (all[i].IsChange)
                yield return all[i];
        }
    }

    private static (char Prefix, string Text)? Transform(DiffLine line, bool selected, SelectedDiffPatchMode mode)
    {
        switch (line.Kind)
        {
            case DiffLineKind.Context:
                return (' ', line.DisplayText);
            case DiffLineKind.Added when selected:
                return ('+', line.DisplayText);
            case DiffLineKind.Removed when selected:
                return ('-', line.DisplayText);
            case DiffLineKind.Added when mode == SelectedDiffPatchMode.MatchOld:
                return null;
            case DiffLineKind.Added:
                return (' ', line.DisplayText);
            case DiffLineKind.Removed when mode == SelectedDiffPatchMode.MatchOld:
                return (' ', line.DisplayText);
            case DiffLineKind.Removed:
                return null;
            default:
                return null;
        }
    }
}

file sealed class DiffLineRefComparer : IEqualityComparer<DiffLine>
{
    public static readonly DiffLineRefComparer Instance = new();
    public bool Equals(DiffLine? x, DiffLine? y) => ReferenceEquals(x, y);
    public int GetHashCode(DiffLine obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
}
