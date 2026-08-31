namespace GitTree.Core;

public static class GitDir
{
    public static string Resolve(string workingDirectory)
    {
        var git = Path.Combine(workingDirectory, ".git");
        if (Directory.Exists(git))
            return git;
        if (!File.Exists(git))
            return git;

        foreach (var raw in File.ReadAllLines(git))
        {
            var line = raw.Trim();
            const string prefix = "gitdir:";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var pointed = line[prefix.Length..].Trim();
            return Path.IsPathRooted(pointed)
                ? Path.GetFullPath(pointed)
                : Path.GetFullPath(Path.Combine(workingDirectory, pointed));
        }

        return git;
    }

    public static string ResolveCommonDir(string gitDir)
    {
        var file = Path.Combine(gitDir, "commondir");
        if (!File.Exists(file))
            return Path.GetFullPath(gitDir);

        var raw = File.ReadAllText(file).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return Path.GetFullPath(gitDir);

        return Path.IsPathRooted(raw)
            ? Path.GetFullPath(raw)
            : Path.GetFullPath(Path.Combine(gitDir, raw));
    }
}
