namespace GitTree.Core;

public sealed class CheckoutChoice
{
    public required string Label { get; init; }
    public required string RefOrSha { get; init; }
    public required bool IsDetached { get; init; }
    public bool IsRemote { get; init; }
    public string LocalName { get; init; } = "";

    public override string ToString() => Label;
}

public static class CheckoutTargets
{
    public static IReadOnlyList<CheckoutChoice> ForCommit(
        string? commitSha,
        IEnumerable<BranchRef> branches)
    {
        if (string.IsNullOrWhiteSpace(commitSha))
            return [];

        var atCommit = branches
            .Where(b => HeadRefParser.ShasMatch(b.TipSha, commitSha))
            .ToList();

        var locals = atCommit
            .Where(b => !b.IsRemote)
            .OrderBy(b => b.IsCurrent ? 0 : 1)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Select(b => new CheckoutChoice
            {
                Label = b.Name,
                RefOrSha = b.Name,
                IsDetached = false,
                LocalName = b.Name
            })
            .ToList();

        var localNames = locals.Select(c => c.LocalName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remotes = new List<CheckoutChoice>();
        foreach (var branch in atCommit
                     .Where(b => b.IsRemote)
                     .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
        {
            var local = LocalNameFromRemote(branch.Name);
            if (local.Length == 0
                || local.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                || !localNames.Add(local))
                continue;

            remotes.Add(new CheckoutChoice
            {
                Label = branch.Name,
                RefOrSha = branch.Name,
                IsDetached = false,
                IsRemote = true,
                LocalName = local
            });
        }

        var matches = locals.Concat(remotes).ToList();
        if (matches.Count > 0)
            return matches;

        return
        [
            new CheckoutChoice
            {
                Label = "detached",
                RefOrSha = commitSha,
                IsDetached = true
            }
        ];
    }

    private static string LocalNameFromRemote(string remoteName)
    {
        var slash = remoteName.IndexOf('/');
        if (slash < 0 || slash == remoteName.Length - 1)
            return remoteName;
        return remoteName[(slash + 1)..];
    }
}
