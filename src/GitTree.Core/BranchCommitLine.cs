namespace GitTree.Core;

/// <summary>
/// Commits made on a branch: the first-parent line from its tip, stopping
/// where that line was branched from something else. Branches that later
/// forked off this line do not shorten it.
/// </summary>
public static class BranchCommitLine
{
    public static HashSet<string> OwnCommits(
        string tipSha,
        IEnumerable<string> otherTipShas,
        IReadOnlyList<CommitNode> commits)
    {
        var bySha = new Dictionary<string, CommitNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var commit in commits)
            bySha.TryAdd(commit.Sha, commit);

        if (!TryResolve(bySha, tipSha, out var tip))
            return [];

        var chain = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = tip;
        while (seen.Add(current) && bySha.TryGetValue(current, out var commit))
        {
            chain.Add(commit.Sha);
            var parent = commit.ParentShas.FirstOrDefault(parent => !string.IsNullOrWhiteSpace(parent));
            if (parent is null || !bySha.ContainsKey(parent))
                break;
            current = bySha[parent].Sha;
        }

        if (chain.Count == 0)
            return [];

        var onChain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < chain.Count; i++)
            onChain[chain[i]] = i;

        var ancestors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> AncestorsOf(string sha)
        {
            if (ancestors.TryGetValue(sha, out var cached))
                return cached;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(sha);
            while (stack.Count > 0)
            {
                var next = stack.Pop();
                if (!set.Add(next) || !bySha.TryGetValue(next, out var node))
                    continue;
                foreach (var parent in node.ParentShas)
                {
                    if (!string.IsNullOrWhiteSpace(parent) && bySha.ContainsKey(parent))
                        stack.Push(bySha[parent].Sha);
                }
            }

            ancestors[sha] = set;
            return set;
        }

        var cuts = new List<int>();
        foreach (var otherTip in otherTipShas)
        {
            if (string.IsNullOrWhiteSpace(otherTip) || ShaEquals(otherTip, tip))
                continue;
            if (!TryResolve(bySha, otherTip, out var other))
                continue;

            var otherAncestors = AncestorsOf(other);
            if (otherAncestors.Contains(tip))
                continue;

            if (onChain.TryGetValue(other, out var pointedAt))
            {
                if (pointedAt > 0)
                    cuts.Add(pointedAt);
                continue;
            }

            for (var i = 1; i < chain.Count; i++)
            {
                if (!otherAncestors.Contains(chain[i]))
                    continue;
                cuts.Add(i);
                break;
            }
        }

        int? cut = null;
        if (cuts.Count > 0)
        {
            // A branch that forked off this line has a nearer cut. The branch
            // this line itself came from is the nearest cut that still has
            // another branch forking closer to the tip. With only one other
            // branch, that single fork is the base.
            var withDescendant = cuts.Where(index => cuts.Any(other => other < index)).ToList();
            cut = (withDescendant.Count > 0 ? withDescendant : cuts).Min();
        }

        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = cut ?? chain.Count;
        for (var i = 0; i < count; i++)
            owned.Add(chain[i]);
        return owned;
    }

    private static bool TryResolve(Dictionary<string, CommitNode> bySha, string sha, out string resolved)
    {
        if (bySha.ContainsKey(sha))
        {
            resolved = bySha[sha].Sha;
            return true;
        }

        var match = bySha.Keys.FirstOrDefault(key =>
            key.StartsWith(sha, StringComparison.OrdinalIgnoreCase)
            || sha.StartsWith(key, StringComparison.OrdinalIgnoreCase));
        resolved = match ?? "";
        return match is not null;
    }

    private static bool ShaEquals(string a, string b) =>
        a.Equals(b, StringComparison.OrdinalIgnoreCase)
        || a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
        || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
}
