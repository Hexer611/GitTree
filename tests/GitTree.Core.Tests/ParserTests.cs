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
        Assert.DoesNotContain(lines, l => l.Kind == DiffLineKind.Meta);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Hunk);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Removed && l.DisplayText == "old" && l.OldNumber == 1);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Added && l.DisplayText == "new" && l.NewNumber == 1);
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Context && l.DisplayText == "same");
    }

    [Fact]
    public void HighlightsChangedWordsOnPairedLines()
    {
        var diff = """
            @@ -1 +1 @@
            -hello world
            +hello there
            """;
        var lines = DiffLineParser.Parse(diff);
        var removed = Assert.Single(lines, l => l.Kind == DiffLineKind.Removed);
        var added = Assert.Single(lines, l => l.Kind == DiffLineKind.Added);
        Assert.Contains(removed.Segments, s => s.Highlight && s.Text.Contains("world"));
        Assert.Contains(added.Segments, s => s.Highlight && s.Text.Contains("there"));
        Assert.Contains(removed.Segments, s => !s.Highlight && s.Text.Contains("hello"));
    }
}

public class WorktreeListParserTests
{
    [Fact]
    public void ParsesPorcelainAndMarksCurrent()
    {
        var porcelain = """
            worktree C:/repos/app
            HEAD abcdef1
            branch refs/heads/main

            worktree C:/repos/app-feature
            HEAD 1234567
            branch refs/heads/feature/login

            worktree C:/repos/app-hotfix
            HEAD 89abcde
            detached
            locked
            """;

        var list = WorktreeListParser.Parse(porcelain, @"C:\repos\app");
        Assert.Equal(3, list.Count);
        Assert.True(list[0].IsCurrent);
        Assert.Equal("main", list[0].Branch);
        Assert.Equal("feature/login", list[1].Branch);
        Assert.False(list[1].IsCurrent);
        Assert.True(list[2].IsDetached);
        Assert.True(list[2].IsLocked);
        Assert.Contains("detached", list[2].Label);
    }
}


public class SlashTreeTests
{
    [Fact]
    public void GroupsBySlashAndKeepsFoldersFirst()
    {
        var tree = SlashTree.Build(new[]
        {
            ("main", 1),
            ("feature/login", 2),
            ("feature/payments/stripe", 3),
            ("origin/main", 4)
        });

        Assert.Equal(3, tree.Count);
        Assert.True(tree[0].IsFolder);
        Assert.Equal("feature", tree[0].Name);
        Assert.Equal("login", tree[0].Children.Single(c => !c.IsFolder).Name);
        var payments = tree[0].Children.Single(c => c.IsFolder);
        Assert.Equal("payments", payments.Name);
        Assert.Equal("stripe", Assert.Single(payments.Children).Name);
        Assert.Equal("origin", tree[1].Name);
        Assert.Equal("main", tree[2].Name);
        Assert.False(tree[2].IsFolder);
    }
}
