using GitTree.Core;
using LibGit2Sharp;

namespace GitTree.Git.LibGit2;

public sealed class LibGit2HistoryReader : IGitHistoryReader
{
    public IReadOnlyList<CommitNode> ReadCommits(string workingDirectory, int maxCount = 400)
    {
        using var repo = new Repository(workingDirectory);
        var decorations = BuildDecorations(repo);

        var commits = new List<CommitNode>();
        foreach (var commit in repo.Commits.QueryBy(new CommitFilter
                 {
                     SortBy = CommitSortStrategies.Time,
                     IncludeReachableFrom = repo.Branches
                 }))
        {
            decorations.TryGetValue(commit.Sha, out var refs);
            commits.Add(new CommitNode
            {
                Sha = commit.Sha,
                ParentShas = commit.Parents.Select(p => p.Sha).ToArray(),
                AuthorName = commit.Author.Name,
                AuthorEmail = commit.Author.Email,
                AuthorDate = commit.Author.When,
                Subject = commit.MessageShort,
                Decorations = refs ?? []
            });

            if (commits.Count >= maxCount)
                break;
        }

        GraphLayout.Assign(commits);
        return commits;
    }

    private static Dictionary<string, List<string>> BuildDecorations(Repository repo)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string sha, string label)
        {
            if (!map.TryGetValue(sha, out var list))
            {
                list = [];
                map[sha] = list;
            }

            if (!list.Contains(label))
                list.Add(label);
        }

        if (repo.Info.IsHeadDetached && repo.Head?.Tip is not null)
            Add(repo.Head.Tip.Sha, "HEAD");

        foreach (var branch in repo.Branches)
        {
            if (branch.Tip is null)
                continue;
            var name = branch.FriendlyName;
            if (string.IsNullOrWhiteSpace(name)
                || name.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("/HEAD", StringComparison.OrdinalIgnoreCase))
                continue;
            Add(branch.Tip.Sha, name);
        }

        foreach (var tag in repo.Tags)
        {
            var sha = (tag.PeeledTarget as Commit)?.Sha ?? tag.Target.Sha;
            Add(sha, $"tag: {tag.FriendlyName}");
        }

        return map;
    }
}
