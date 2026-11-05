namespace GitTree.Core;

public readonly record struct BranchPushTarget(string Remote, string Branch)
{
    public string Display => $"{Remote}/{Branch}";

    public string Refspec(string localBranch) => $"{localBranch}:{Branch}";
}

public static class BranchPush
{
    public static BranchPushTarget? FromConfig(string? remote, string? merge)
    {
        var branch = BranchRefNames.BranchFromMerge(merge);
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(branch))
            return null;
        return new BranchPushTarget(remote.Trim(), branch);
    }

    public static BranchPushTarget? FromUpstream(string? upstream, IReadOnlyList<RemoteInfo> remotes)
    {
        if (string.IsNullOrWhiteSpace(upstream))
            return null;
        var (remote, branch) = PullRequestUrls.SplitRemoteRef(upstream.Trim(), remotes);
        if (string.IsNullOrWhiteSpace(remote) || string.IsNullOrWhiteSpace(branch))
            return null;
        return new BranchPushTarget(remote, branch);
    }

    public static string? DistinctLabel(string localName, string? upstream, IReadOnlyList<RemoteInfo> remotes)
    {
        var target = FromUpstream(upstream, remotes);
        if (target is null)
            return null;
        if (target.Value.Branch.Equals(localName, StringComparison.Ordinal))
            return null;
        return target.Value.Display;
    }

    public static IReadOnlyList<string> Arguments(BranchPushTarget? target, string? localBranch)
    {
        if (target is null
            || string.IsNullOrWhiteSpace(localBranch)
            || localBranch.Equals("HEAD", StringComparison.Ordinal))
            return ["push", "-u", "origin", "HEAD"];
        return ["push", target.Value.Remote, target.Value.Refspec(localBranch)];
    }

    public static Dictionary<string, BranchPushTarget> ParseConfig(string output)
    {
        var remotes = new Dictionary<string, string>(StringComparer.Ordinal);
        var merges = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var line = raw.Trim();
            var split = line.IndexOfAny([' ', '\t']);
            if (split <= 0)
                continue;
            var key = line[..split];
            var value = line[(split + 1)..].Trim();
            if (!key.StartsWith("branch.", StringComparison.Ordinal) || value.Length == 0)
                continue;

            var body = key["branch.".Length..];
            if (body.EndsWith(".remote", StringComparison.Ordinal))
                remotes[body[..^".remote".Length]] = value;
            else if (body.EndsWith(".merge", StringComparison.Ordinal))
                merges[body[..^".merge".Length]] = value;
        }

        var configured = new Dictionary<string, BranchPushTarget>(StringComparer.Ordinal);
        foreach (var (name, remote) in remotes)
        {
            if (!merges.TryGetValue(name, out var merge))
                continue;
            var target = FromConfig(remote, merge);
            if (target is not null)
                configured[name] = target.Value;
        }

        return configured;
    }

    public static IReadOnlyList<BranchRef> Apply(
        IReadOnlyList<BranchRef> branches,
        IReadOnlyDictionary<string, BranchPushTarget> configured,
        IReadOnlyList<RemoteInfo> remotes)
    {
        var list = new List<BranchRef>(branches.Count);
        foreach (var branch in branches)
        {
            if (branch.IsRemote)
            {
                list.Add(branch);
                continue;
            }

            var upstream = branch.Upstream;
            if (string.IsNullOrWhiteSpace(upstream) && configured.TryGetValue(branch.Name, out var target))
                upstream = target.Display;

            var label = DistinctLabel(branch.Name, upstream, remotes);
            if (string.Equals(upstream, branch.Upstream, StringComparison.Ordinal)
                && string.Equals(label, branch.PushTargetLabel, StringComparison.Ordinal))
            {
                list.Add(branch);
                continue;
            }

            list.Add(branch.WithPushTarget(upstream, label));
        }

        return list;
    }
}
