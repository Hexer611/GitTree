namespace GitTree.Core;

public sealed class HeadState
{
    public required bool IsDetached { get; init; }
    public required string CurrentBranch { get; init; }
}

public static class HeadRefParser
{
    public static string? TryRead(string gitDir)
    {
        try
        {
            var path = Path.Combine(gitDir, "HEAD");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static HeadState Resolve(
        string? headFileContents,
        string? abbrevRef,
        string? headSha,
        string? currentBranchFromRefs)
    {
        if (TryParseSymbolic(headFileContents, out var fromFile) && fromFile is not null)
            return fromFile;

        if (!string.IsNullOrWhiteSpace(currentBranchFromRefs)
            && !IsDetachedName(currentBranchFromRefs))
        {
            return new HeadState
            {
                IsDetached = false,
                CurrentBranch = currentBranchFromRefs.Trim()
            };
        }

        var abbrev = abbrevRef?.Trim() ?? "";
        if (abbrev.Length > 0 && !IsDetachedName(abbrev))
        {
            return new HeadState
            {
                IsDetached = false,
                CurrentBranch = abbrev
            };
        }

        if (TryParseDetached(headFileContents, headSha, out var detached))
            return detached;

        return Detached(headSha);
    }

    public static HeadState ParseFile(string contents, string? headSha = null)
    {
        if (TryParseSymbolic(contents, out var attached) && attached is not null)
            return attached;
        if (TryParseDetached(contents, headSha, out var detached))
            return detached;
        return Detached(headSha);
    }

    public static void ApplyToCommits(IReadOnlyList<CommitNode> commits, HeadState head, string headSha)
    {
        foreach (var commit in commits)
        {
            commit.IsHead = ShasMatch(commit.Sha, headSha);
            var decorations = commit.Decorations
                .Select(GitLogParser.CleanDecoration)
                .Where(s => s.Length > 0)
                .Where(s => head.IsDetached || !IsDetachedName(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (head.IsDetached && commit.IsHead && !decorations.Contains("HEAD", StringComparer.Ordinal))
                decorations.Insert(0, "HEAD");

            commit.Decorations = decorations;
        }
    }

    public static bool ShasMatch(string sha, string headSha)
    {
        if (string.IsNullOrWhiteSpace(sha) || string.IsNullOrWhiteSpace(headSha))
            return false;
        return sha.Equals(headSha, StringComparison.OrdinalIgnoreCase)
               || (headSha.Length >= 4 && sha.StartsWith(headSha, StringComparison.OrdinalIgnoreCase))
               || (sha.Length >= 4 && headSha.StartsWith(sha, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParseSymbolic(string? contents, out HeadState? state)
    {
        state = null;
        var text = Normalize(contents);
        if (!text.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            return false;

        var full = text[4..].Trim();
        if (string.IsNullOrWhiteSpace(full))
            return false;

        state = new HeadState
        {
            IsDetached = false,
            CurrentBranch = Shorten(full)
        };
        return true;
    }

    private static bool TryParseDetached(string? contents, string? headSha, out HeadState state)
    {
        var text = Normalize(contents);
        if (text.Length == 0 || text.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
        {
            state = Detached(headSha);
            return false;
        }

        state = Detached(text);
        return true;
    }

    private static HeadState Detached(string? sha)
    {
        var value = (sha ?? "").Trim();
        var shortSha = value.Length >= 7 ? value[..7] : value;
        return new HeadState
        {
            IsDetached = true,
            CurrentBranch = string.IsNullOrEmpty(shortSha) ? "detached" : $"detached {shortSha}"
        };
    }

    private static bool IsDetachedName(string value) =>
        value.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("HEAD ->", StringComparison.OrdinalIgnoreCase)
        || value.Equals("detached", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("detached ", StringComparison.OrdinalIgnoreCase);

    private static string Shorten(string fullRef)
    {
        if (fullRef.StartsWith("refs/heads/", StringComparison.Ordinal))
            return fullRef["refs/heads/".Length..];
        if (fullRef.StartsWith("refs/remotes/", StringComparison.Ordinal))
            return fullRef["refs/remotes/".Length..];
        if (fullRef.StartsWith("refs/tags/", StringComparison.Ordinal))
            return fullRef["refs/tags/".Length..];
        return fullRef;
    }

    private static string Normalize(string? contents) =>
        (contents ?? "").Trim().TrimStart('\uFEFF');
}
