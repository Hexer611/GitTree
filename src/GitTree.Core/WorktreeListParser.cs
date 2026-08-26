namespace GitTree.Core;

public static class WorktreeListParser
{
    public static IReadOnlyList<WorktreeInfo> Parse(string porcelain, string currentWorkingDirectory)
    {
        var list = new List<WorktreeInfo>();
        if (string.IsNullOrWhiteSpace(porcelain))
            return list;

        var current = Normalize(currentWorkingDirectory);
        string? path = null;
        var head = "";
        string? branch = null;
        var detached = false;
        var bare = false;
        var locked = false;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            list.Add(new WorktreeInfo
            {
                Path = path,
                HeadSha = head,
                Branch = branch,
                IsDetached = detached || string.IsNullOrEmpty(branch),
                IsBare = bare,
                IsLocked = locked,
                IsCurrent = string.Equals(Normalize(path), current, StringComparison.OrdinalIgnoreCase)
            });
            path = null;
            head = "";
            branch = null;
            detached = false;
            bare = false;
            locked = false;
        }

        foreach (var raw in porcelain.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith("worktree ", StringComparison.Ordinal))
            {
                Flush();
                path = line["worktree ".Length..].Trim();
            }
            else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                head = line["HEAD ".Length..].Trim();
            else if (line.StartsWith("branch ", StringComparison.Ordinal))
            {
                var full = line["branch ".Length..].Trim();
                branch = full.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? full["refs/heads/".Length..]
                    : full;
            }
            else if (line.Equals("detached", StringComparison.Ordinal))
                detached = true;
            else if (line.Equals("bare", StringComparison.Ordinal))
                bare = true;
            else if (line.StartsWith("locked", StringComparison.Ordinal))
                locked = true;
        }

        Flush();
        return list;
    }

    private static string Normalize(string path) =>
        System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
}
