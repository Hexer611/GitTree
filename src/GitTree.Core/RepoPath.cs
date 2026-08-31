namespace GitTree.Core;

public static class GitRepositoryLocator
{
    public static string? FindRoot(string path)
    {
        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(Path.GetFullPath(path));
        }
        catch
        {
            return null;
        }

        while (dir is not null)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// The main worktree of the repository that contains <paramref name="path"/>.
    /// Linked worktrees resolve to that primary checkout, not their own folder.
    /// </summary>
    public static string? FindProjectRoot(string path)
    {
        var root = FindRoot(path);
        if (root is null)
            return null;

        try
        {
            var gitDir = GitDir.Resolve(root);
            var common = GitDir.ResolveCommonDir(gitDir);
            var main = MainWorktreeFromCommonDir(common);
            return main ?? root;
        }
        catch
        {
            return root;
        }
    }

    public static bool IsSameProject(string pathA, string pathB)
    {
        if (string.IsNullOrWhiteSpace(pathA) || string.IsNullOrWhiteSpace(pathB))
            return false;
        if (PathsEqual(pathA, pathB))
            return true;

        try
        {
            var commonA = TryCommonDir(pathA);
            var commonB = TryCommonDir(pathB);
            if (commonA is not null && commonB is not null && PathsEqual(commonA, commonB))
                return true;
        }
        catch
        {
            // ignored — fall through to name matching
        }

        var nameA = ProjectIdentityName(pathA);
        var nameB = ProjectIdentityName(pathB);
        return nameA is not null
            && nameB is not null
            && nameA.Equals(nameB, StringComparison.OrdinalIgnoreCase)
            && (IsAuxiliaryWorktreePath(pathA) || IsAuxiliaryWorktreePath(pathB));
    }

    public static bool PathsEqual(string pathA, string pathB) =>
        string.Equals(Normalize(pathA), Normalize(pathB), StringComparison.OrdinalIgnoreCase);

    public static bool IsAuxiliaryWorktreePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        if (IsCursorWorktreePath(path))
            return true;

        try
        {
            var full = Normalize(path);
            if (!Directory.Exists(full))
                return false;
            return IsLinkedGitWorktree(full);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsCursorWorktreePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/.cursor/worktrees/", StringComparison.OrdinalIgnoreCase);
    }

    public static string? CursorWorktreeProjectName(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals(".cursor", StringComparison.OrdinalIgnoreCase)
                && parts[i + 1].Equals("worktrees", StringComparison.OrdinalIgnoreCase)
                && i + 2 < parts.Length)
                return parts[i + 2];
        }

        return null;
    }

    public static bool IsLinkedGitWorktree(string workingDirectory)
    {
        var git = Path.Combine(workingDirectory, ".git");
        if (Directory.Exists(git) || !File.Exists(git))
            return false;

        var gitDir = GitDir.Resolve(workingDirectory);
        if (File.Exists(Path.Combine(gitDir, "commondir")))
            return true;

        return gitDir.Contains($"{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || gitDir.Contains("/worktrees/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Project checkout to remember in the top-bar picker. Returns null for a worktree
    /// that cannot be mapped to a real project — callers must not store the worktree path.
    /// </summary>
    public static string? ResolveProjectForRecents(string path, IEnumerable<string>? knownProjects = null)
    {
        var known = CollectKnownProjects(knownProjects);

        if (!IsAuxiliaryWorktreePath(path))
        {
            var project = FindProjectRoot(path) ?? Normalize(path);
            return IsAuxiliaryWorktreePath(project) ? MapWorktreeToProject(path, known) : project;
        }

        return MapWorktreeToProject(path, known);
    }

    public static List<string> CollapseToProjects(IEnumerable<string> paths, int max = 12)
    {
        var all = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var known = CollectKnownProjects(all);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in all)
        {
            var project = ResolveProjectForRecents(path, known);
            if (string.IsNullOrWhiteSpace(project) || IsAuxiliaryWorktreePath(project))
                continue;
            if (!seen.Add(Normalize(project)))
                continue;

            result.Add(project);
            if (result.Count >= max)
                break;
        }

        return result;
    }

    public static string FolderName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private static List<string> CollectKnownProjects(IEnumerable<string>? paths)
    {
        var known = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paths is null)
            return known;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || IsAuxiliaryWorktreePath(path))
                continue;
            var project = FindProjectRoot(path) ?? Normalize(path);
            if (IsAuxiliaryWorktreePath(project) || !seen.Add(Normalize(project)))
                continue;
            known.Add(project);
        }

        return known;
    }

    private static string? MapWorktreeToProject(string path, IReadOnlyList<string> knownProjects)
    {
        var resolved = FindProjectRoot(path);
        if (resolved is not null && !IsAuxiliaryWorktreePath(resolved))
            return resolved;

        var name = CursorWorktreeProjectName(path);
        if (string.IsNullOrEmpty(name))
            return null;

        foreach (var known in knownProjects)
        {
            if (FolderName(known).Equals(name, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        return null;
    }

    private static string? TryCommonDir(string path)
    {
        var root = FindRoot(path);
        if (root is null)
            return null;
        return GitDir.ResolveCommonDir(GitDir.Resolve(root));
    }

    private static string? ProjectIdentityName(string path)
    {
        return CursorWorktreeProjectName(path)
               ?? FolderName(FindProjectRoot(path) ?? path);
    }

    private static string? MainWorktreeFromCommonDir(string commonDir)
    {
        var trimmed = commonDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.GetFileName(trimmed).Equals(".git", StringComparison.OrdinalIgnoreCase))
            return null;

        var parent = Path.GetDirectoryName(trimmed);
        return string.IsNullOrEmpty(parent) ? null : Path.GetFullPath(parent);
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
