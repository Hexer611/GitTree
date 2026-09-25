namespace GitTree.Core;

public enum RemoteCheckoutKind
{
    CreateLocal,
    CheckoutLocal,
    Conflict
}

public enum RemoteCheckoutRelation
{
    Behind,
    Ahead,
    Diverged,
    Different
}

public enum RemoteCheckoutConflictAction
{
    ReplaceLocal,
    CheckoutLocal,
    DetachAtRemote
}

public sealed class RemoteCheckoutPlan
{
    public required RemoteCheckoutKind Kind { get; init; }
    public required string RemoteRef { get; init; }
    public required string LocalName { get; init; }
    public BranchRef? Local { get; init; }
    public RemoteCheckoutRelation Relation { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    public string Summary { get; init; } = "";
}

public sealed class RemoteCheckoutOption
{
    public required RemoteCheckoutConflictAction Action { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required bool IsDestructive { get; init; }

    public override string ToString() => Title;
}

public static class RemoteCheckout
{
    public static string LocalNameFromRemote(string remoteName)
    {
        var slash = remoteName.IndexOf('/');
        if (slash < 0 || slash == remoteName.Length - 1)
            return remoteName;
        return remoteName[(slash + 1)..];
    }

    public static RemoteCheckoutPlan For(
        string remoteRef,
        IEnumerable<BranchRef> branches,
        IEnumerable<CommitNode>? commits = null)
    {
        var localName = LocalNameFromRemote(remoteRef);
        var list = branches as IReadOnlyList<BranchRef> ?? branches.ToList();
        var local = list.FirstOrDefault(b =>
            !b.IsRemote && b.Name.Equals(localName, StringComparison.OrdinalIgnoreCase));
        var remote = list.FirstOrDefault(b =>
            b.IsRemote && b.Name.Equals(remoteRef, StringComparison.OrdinalIgnoreCase));

        if (local is null)
        {
            return new RemoteCheckoutPlan
            {
                Kind = RemoteCheckoutKind.CreateLocal,
                RemoteRef = remoteRef,
                LocalName = localName
            };
        }

        if (remote is not null && HeadRefParser.ShasMatch(local.TipSha, remote.TipSha))
        {
            return new RemoteCheckoutPlan
            {
                Kind = RemoteCheckoutKind.CheckoutLocal,
                RemoteRef = remoteRef,
                LocalName = localName,
                Local = local
            };
        }

        var tracks = Tracks(local, remoteRef);
        var ahead = tracks ? local.Ahead : 0;
        var behind = tracks ? local.Behind : 0;
        var relation = Classify(tracks, ahead, behind);
        if (relation == RemoteCheckoutRelation.Different)
            (relation, ahead, behind) = InferFromGraph(local.TipSha, remote?.TipSha, commits);

        return new RemoteCheckoutPlan
        {
            Kind = RemoteCheckoutKind.Conflict,
            RemoteRef = remoteRef,
            LocalName = localName,
            Local = local,
            Relation = relation,
            Ahead = ahead,
            Behind = behind,
            Summary = BuildSummary(localName, remoteRef, relation, ahead, behind)
        };
    }

    public static IReadOnlyList<RemoteCheckoutOption> ConflictOptions(RemoteCheckoutPlan plan)
    {
        var local = plan.LocalName;
        var remote = plan.RemoteRef;
        var destructive = plan.Relation != RemoteCheckoutRelation.Behind;
        return
        [
            new RemoteCheckoutOption
            {
                Action = RemoteCheckoutConflictAction.ReplaceLocal,
                Title = $"Replace local '{local}' with {remote}",
                Description = ReplaceDescription(plan, local, remote),
                IsDestructive = destructive
            },
            new RemoteCheckoutOption
            {
                Action = RemoteCheckoutConflictAction.CheckoutLocal,
                Title = $"Checkout existing local '{local}'",
                Description = CheckoutLocalDescription(plan),
                IsDestructive = false
            },
            new RemoteCheckoutOption
            {
                Action = RemoteCheckoutConflictAction.DetachAtRemote,
                Title = $"Checkout {remote} as detached HEAD",
                Description = $"Leave local '{local}' unchanged and detach at the remote commit.",
                IsDestructive = false
            }
        ];
    }

    public static RemoteCheckoutOption DefaultOption(
        RemoteCheckoutPlan plan,
        IReadOnlyList<RemoteCheckoutOption> options)
    {
        var preferred = plan.Relation == RemoteCheckoutRelation.Behind
            ? RemoteCheckoutConflictAction.ReplaceLocal
            : RemoteCheckoutConflictAction.CheckoutLocal;
        return options.FirstOrDefault(o => o.Action == preferred) ?? options[0];
    }

    private static bool Tracks(BranchRef local, string remoteRef) =>
        !string.IsNullOrWhiteSpace(local.Upstream)
        && local.Upstream.Equals(remoteRef, StringComparison.OrdinalIgnoreCase);

    private static RemoteCheckoutRelation Classify(bool tracks, int ahead, int behind)
    {
        if (!tracks)
            return RemoteCheckoutRelation.Different;
        if (behind > 0 && ahead == 0)
            return RemoteCheckoutRelation.Behind;
        if (ahead > 0 && behind == 0)
            return RemoteCheckoutRelation.Ahead;
        if (ahead > 0 && behind > 0)
            return RemoteCheckoutRelation.Diverged;
        return RemoteCheckoutRelation.Different;
    }

    private static string BuildSummary(
        string local,
        string remote,
        RemoteCheckoutRelation relation,
        int ahead,
        int behind) =>
        relation switch
        {
            RemoteCheckoutRelation.Behind when behind > 0 =>
                $"A local branch named '{local}' already exists and is {CommitCount(behind)} behind {remote}.",
            RemoteCheckoutRelation.Behind =>
                $"A local branch named '{local}' already exists and is behind {remote}.",
            RemoteCheckoutRelation.Ahead when ahead > 0 =>
                $"A local branch named '{local}' already exists and is {CommitCount(ahead)} ahead of {remote}.",
            RemoteCheckoutRelation.Ahead =>
                $"A local branch named '{local}' already exists and is ahead of {remote}.",
            RemoteCheckoutRelation.Diverged when ahead > 0 || behind > 0 =>
                $"Local branch '{local}' has diverged from {remote} ({ahead} ahead, {behind} behind).",
            RemoteCheckoutRelation.Diverged =>
                $"Local branch '{local}' has diverged from {remote}.",
            _ =>
                $"A local branch named '{local}' already exists at a different commit than {remote}."
        };

    private static string ReplaceDescription(RemoteCheckoutPlan plan, string local, string remote) =>
        plan.Relation switch
        {
            RemoteCheckoutRelation.Behind =>
                $"Move the local branch to the remote tip and check it out. Same result as deleting the behind local branch and creating it again from {remote}.",
            RemoteCheckoutRelation.Ahead or RemoteCheckoutRelation.Diverged =>
                $"Reset local '{local}' to {remote} and check it out. Commits that exist only on the local branch will no longer be on '{local}'.",
            _ =>
                $"Reset local '{local}' to {remote} and check it out."
        };

    private static string CheckoutLocalDescription(RemoteCheckoutPlan plan) =>
        plan.Relation switch
        {
            RemoteCheckoutRelation.Behind =>
                "Switch to the local branch as it is. It stays behind the remote.",
            RemoteCheckoutRelation.Ahead =>
                "Switch to the local branch as it is, keeping the local commits.",
            RemoteCheckoutRelation.Diverged =>
                "Switch to the local branch as it is, without changing its tip.",
            _ =>
                "Switch to the existing local branch without moving it."
        };

    private static string CommitCount(int count) =>
        count == 1 ? "1 commit" : $"{count} commits";

    private static (RemoteCheckoutRelation Relation, int Ahead, int Behind) InferFromGraph(
        string localSha,
        string? remoteSha,
        IEnumerable<CommitNode>? commits)
    {
        if (string.IsNullOrWhiteSpace(remoteSha) || commits is null)
            return (RemoteCheckoutRelation.Different, 0, 0);

        var nodes = commits as IReadOnlyList<CommitNode> ?? commits.ToList();
        if (nodes.Count == 0)
            return (RemoteCheckoutRelation.Different, 0, 0);

        var bySha = new Dictionary<string, CommitNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var commit in nodes)
        {
            if (!bySha.ContainsKey(commit.Sha))
                bySha[commit.Sha] = commit;
        }

        if (!TryFind(bySha, localSha, out _) || !TryFind(bySha, remoteSha, out _))
            return (RemoteCheckoutRelation.Different, 0, 0);

        var behind = Distance(localSha, remoteSha, bySha);
        var ahead = Distance(remoteSha, localSha, bySha);
        if (behind is > 0 && ahead is null)
            return (RemoteCheckoutRelation.Behind, 0, behind.Value);
        if (ahead is > 0 && behind is null)
            return (RemoteCheckoutRelation.Ahead, ahead.Value, 0);
        return (RemoteCheckoutRelation.Diverged, ahead ?? 0, behind ?? 0);
    }

    private static int? Distance(
        string ancestorSha,
        string descendantSha,
        Dictionary<string, CommitNode> bySha)
    {
        if (!TryFind(bySha, descendantSha, out var start))
            return null;

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(CommitNode Node, int Depth)>();
        queue.Enqueue((start, 0));
        visited.Add(start.Sha);

        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (HeadRefParser.ShasMatch(node.Sha, ancestorSha))
                return depth;

            foreach (var parentSha in node.ParentShas)
            {
                if (!TryFind(bySha, parentSha, out var parent) || !visited.Add(parent.Sha))
                    continue;
                queue.Enqueue((parent, depth + 1));
            }
        }

        return null;
    }

    private static bool TryFind(
        Dictionary<string, CommitNode> bySha,
        string sha,
        out CommitNode node)
    {
        if (bySha.TryGetValue(sha, out node!))
            return true;

        foreach (var pair in bySha)
        {
            if (HeadRefParser.ShasMatch(pair.Key, sha))
            {
                node = pair.Value;
                return true;
            }
        }

        node = null!;
        return false;
    }
}
