using GitTree.Core;

namespace GitTree.Core.Tests;

public class StatusPorcelainParserTests
{
    [Fact]
    public void ParsesStagedUnstagedUntrackedAndConflict()
    {
        var porcelain = """
            ## main
             M src/a.cs
            M  src/b.cs
            MM src/c.cs
            ?? new.txt
            UU conflict.cs
            R  old.txt -> renamed.txt
            """;

        var changes = StatusPorcelainParser.Parse(porcelain);
        Assert.Equal(6, changes.Count);

        var a = changes.Single(c => c.Path == "src/a.cs");
        Assert.True(a.IsUnstaged);
        Assert.False(a.IsStaged);
        Assert.Equal(FileChangeKind.Modified, a.WorkTreeStatus);

        var b = changes.Single(c => c.Path == "src/b.cs");
        Assert.True(b.IsStaged);
        Assert.False(b.IsUnstaged);

        var conflict = changes.Single(c => c.Path == "conflict.cs");
        Assert.True(conflict.IsConflict);

        var renamed = changes.Single(c => c.Path == "renamed.txt");
        Assert.Equal("old.txt", renamed.OldPath);
        Assert.Equal(FileChangeKind.Renamed, renamed.IndexStatus);

        var untracked = changes.Single(c => c.Path == "new.txt");
        Assert.Equal(FileChangeKind.Untracked, untracked.IndexStatus);
        Assert.True(untracked.IsUnstaged);
    }
}

public class GraphLayoutTests
{
    [Fact]
    public void AssignsFirstParentToSameLane()
    {
        var commits = new List<CommitNode>
        {
            Node("c2", "c1"),
            Node("c1")
        };

        GraphLayout.Assign(commits);
        Assert.Equal(0, commits[0].Lane);
        Assert.Equal(0, commits[1].Lane);
        Assert.Contains(commits[0].Edges, e => e.FromLane == 0 && e.ToLane == 0);
    }

    [Fact]
    public void MergeUsesSecondLane()
    {
        var commits = new List<CommitNode>
        {
            Node("m", "a", "b"),
            Node("a", "r"),
            Node("b", "r"),
            Node("r")
        };

        GraphLayout.Assign(commits);
        Assert.Equal(0, commits[0].Lane);
        Assert.True(commits[0].Edges.Count >= 2);
        Assert.Contains(commits[0].Edges, e => e.ToLane != commits[0].Lane || e.FromLane == 0);
    }

    private static CommitNode Node(string sha, params string[] parents) => new()
    {
        Sha = sha,
        ParentShas = parents,
        AuthorName = "t",
        AuthorEmail = "t@t",
        AuthorDate = DateTimeOffset.UnixEpoch,
        Subject = sha
    };
}

public class GitLogParserTests
{
    [Fact]
    public void ParsesRecordsAndDecorations()
    {
        var output = $"abc1234\u001fparent1\u001fAda\u001fada@ex.com\u001f2024-01-02T03:04:05+00:00\u001fHello\u001fHEAD -> main, origin/main\u001e";
        var commits = GitLogParser.Parse(output);
        var c = Assert.Single(commits);
        Assert.Equal("abc1234", c.Sha);
        Assert.Equal("Hello", c.Subject);
        Assert.Contains("main", c.Decorations);
        Assert.Contains("origin/main", c.Decorations);
    }
}

public class DiffLineParserTests
{
    [Fact]
    public void ClassifiesDiffLines()
    {
        var diff = """
            diff --git a/a.txt b/a.txt
            --- a/a.txt
            +++ b/a.txt
            @@ -1 +1 @@
            -old
            +new
             same
            """;
        var lines = DiffLineParser.Parse(diff);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Meta);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Hunk);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Removed && l.Text.Contains("old"));
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Added && l.Text.Contains("new"));
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Context);
    }
}
