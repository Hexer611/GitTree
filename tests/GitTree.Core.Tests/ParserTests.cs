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
        Assert.True(commits[0].TrackCount >= 2);
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
        Assert.DoesNotContain("HEAD", c.Decorations);
        Assert.DoesNotContain("HEAD -> main", c.Decorations);
    }

    [Fact]
    public void KeepsHeadDecorationWhenDetached()
    {
        var output = $"abc1234\u001f\u001fAda\u001fada@ex.com\u001f2024-01-02T03:04:05+00:00\u001fHello\u001fHEAD, tag: v1\u001e";
        var commits = GitLogParser.Parse(output);
        var c = Assert.Single(commits);
        Assert.Contains("HEAD", c.Decorations);
        Assert.Contains("tag: v1", c.Decorations);
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
    public void BuildsStagePatchFromSelectedAddedLine()
    {
        var diff = """
            @@ -1,2 +1,4 @@
             keep
            +take
            +leave
             end
            """;
        var lines = DiffLineParser.Parse(diff);
        var take = Assert.Single(lines, l => l.Kind == DiffLineKind.Added && l.DisplayText == "take");
        var patch = SelectedDiffPatch.Build(lines, [take], "a.txt", SelectedDiffPatchMode.MatchOld);
        Assert.Contains("+take", patch);
        Assert.DoesNotContain("+leave", patch);
        Assert.DoesNotContain(" leave", patch);
        Assert.Contains("@@ -1,2 +1,3 @@", patch);
    }

    [Fact]
    public void BuildsDiscardPatchFromSelectedAddedLine()
    {
        var diff = """
            @@ -1,2 +1,4 @@
             keep
            +take
            +leave
             end
            """;
        var lines = DiffLineParser.Parse(diff);
        var take = Assert.Single(lines, l => l.Kind == DiffLineKind.Added && l.DisplayText == "take");
        var patch = SelectedDiffPatch.Build(lines, [take], "a.txt", SelectedDiffPatchMode.MatchNew);
        Assert.Contains("+take", patch);
        Assert.DoesNotContain("+leave", patch);
        Assert.Contains(" leave", patch);
    }

    [Fact]
    public void ConvertsUnselectedDeletionsToContextWhenStaging()
    {
        var diff = """
            @@ -1,4 +1,2 @@
             keep
            -take
            -leave
             end
            """;
        var lines = DiffLineParser.Parse(diff);
        var take = Assert.Single(lines, l => l.Kind == DiffLineKind.Removed && l.DisplayText == "take");
        var patch = SelectedDiffPatch.Build(lines, [take], "a.txt", SelectedDiffPatchMode.MatchOld);
        Assert.Contains("-take", patch);
        Assert.Contains(" leave", patch);
        Assert.DoesNotContain("-leave", patch);
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

public class SyncStatusParserTests
{
    [Fact]
    public void ParsesAheadAndBehind()
    {
        var sync = SyncStatusParser.ParsePorcelain("## main...origin/main [ahead 2, behind 3]\n M file.txt\n");
        Assert.Equal("main", sync.Branch);
        Assert.Equal("origin/main", sync.Upstream);
        Assert.Equal(2, sync.Ahead);
        Assert.Equal(3, sync.Behind);
        Assert.True(sync.HasAhead);
        Assert.True(sync.HasBehind);
    }

    [Fact]
    public void ParsesInSync()
    {
        var sync = SyncStatusParser.ParsePorcelain("## main...origin/main");
        Assert.True(sync.HasUpstream);
        Assert.True(sync.IsInSync);
    }

    [Fact]
    public void ParsesNoUpstream()
    {
        var sync = SyncStatusParser.ParsePorcelain("## feature/local");
        Assert.Equal("feature/local", sync.Branch);
        Assert.False(sync.HasUpstream);
        Assert.Equal(0, sync.Ahead);
    }

    [Fact]
    public void ParsesDetachedAndUnbornHeaders()
    {
        var detached = SyncStatusParser.ParsePorcelain("## HEAD (no branch)\n");
        Assert.Equal("HEAD", detached.Branch);
        Assert.False(detached.HasUpstream);

        var unborn = SyncStatusParser.ParsePorcelain("## No commits yet on main\n");
        Assert.Equal("main", unborn.Branch);
    }
}

public class HeadRefParserTests
{
    [Fact]
    public void ReadsAttachedBranchFromHeadFile()
    {
        var state = HeadRefParser.ParseFile("ref: refs/heads/feature/login\n");
        Assert.False(state.IsDetached);
        Assert.Equal("feature/login", state.CurrentBranch);
    }

    [Fact]
    public void ReadsDetachedShaFromHeadFile()
    {
        var state = HeadRefParser.ParseFile("abcdef1234567890\n");
        Assert.True(state.IsDetached);
        Assert.Equal("detached abcdef1", state.CurrentBranch);
    }

    [Fact]
    public void PrefersHeadFileOverFailedAbbrevRef()
    {
        var state = HeadRefParser.Resolve("ref: refs/heads/main\n", "HEAD", "", null);
        Assert.False(state.IsDetached);
        Assert.Equal("main", state.CurrentBranch);
    }

    [Fact]
    public void UsesForEachRefWhenHeadFileMissing()
    {
        var state = HeadRefParser.Resolve(null, "HEAD", "abc1234", "main");
        Assert.False(state.IsDetached);
        Assert.Equal("main", state.CurrentBranch);
    }

    [Fact]
    public void StripsHeadChipWhenAttached()
    {
        var commit = new CommitNode
        {
            Sha = "abcdef1",
            ParentShas = [],
            AuthorName = "a",
            AuthorEmail = "a@a",
            AuthorDate = DateTimeOffset.UnixEpoch,
            Subject = "s",
            Decorations = ["HEAD", "HEAD -> main", "origin/main"]
        };
        var head = new HeadState { IsDetached = false, CurrentBranch = "main" };
        HeadRefParser.ApplyToCommits([commit], head, "abcdef1");
        Assert.True(commit.IsHead);
        Assert.Contains("main", commit.Decorations);
        Assert.Contains("origin/main", commit.Decorations);
        Assert.DoesNotContain("HEAD", commit.Decorations);
        Assert.DoesNotContain("HEAD -> main", commit.Decorations);
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

public class CheckoutTargetsTests
{
    [Fact]
    public void ListsLocalBranchesAtCommit()
    {
        var choices = CheckoutTargets.ForCommit("aaa1111",
        [
            Branch("feature", "aaa1111"),
            Branch("main", "aaa1111", isCurrent: true),
            Branch("other", "bbb2222")
        ]);

        Assert.Equal(2, choices.Count);
        Assert.Equal("main", choices[0].Label);
        Assert.Equal("feature", choices[1].Label);
        Assert.All(choices, c => Assert.False(c.IsDetached));
    }

    [Fact]
    public void ListsRemoteBranchesWhenNoLocalMatch()
    {
        var choices = CheckoutTargets.ForCommit("aaa1111",
        [
            Branch("origin/feature", "aaa1111", isRemote: true),
            Branch("origin/HEAD", "aaa1111", isRemote: true),
            Branch("main", "bbb2222", isCurrent: true)
        ]);

        var choice = Assert.Single(choices);
        Assert.True(choice.IsRemote);
        Assert.False(choice.IsDetached);
        Assert.Equal("origin/feature", choice.Label);
        Assert.Equal("feature", choice.LocalName);
    }

    [Fact]
    public void SkipsRemoteWhenLocalWithSameNameIsAlreadyAtCommit()
    {
        var choices = CheckoutTargets.ForCommit("aaa1111",
        [
            Branch("main", "aaa1111", isCurrent: true),
            Branch("origin/main", "aaa1111", isRemote: true)
        ]);

        var choice = Assert.Single(choices);
        Assert.False(choice.IsRemote);
        Assert.Equal("main", choice.Label);
    }

    [Fact]
    public void IncludesRemoteWhenLocalExistsOnADifferentCommit()
    {
        var choices = CheckoutTargets.ForCommit("aaa1111",
        [
            Branch("feature", "bbb2222", isCurrent: true),
            Branch("origin/feature", "aaa1111", isRemote: true)
        ]);

        var choice = Assert.Single(choices);
        Assert.True(choice.IsRemote);
        Assert.Equal("origin/feature", choice.Label);
    }

    [Fact]
    public void OnlyDetachedWhenNoBranchesPointHere()
    {
        var choices = CheckoutTargets.ForCommit("ccc3333",
        [
            Branch("main", "aaa1111", isCurrent: true)
        ]);

        var choice = Assert.Single(choices);
        Assert.True(choice.IsDetached);
        Assert.Equal("detached", choice.Label);
        Assert.Equal("ccc3333", choice.RefOrSha);
    }

    private static BranchRef Branch(string name, string sha, bool isCurrent = false, bool isRemote = false) => new()
    {
        Name = name,
        FullName = isRemote ? $"refs/remotes/{name}" : $"refs/heads/{name}",
        TipSha = sha,
        IsRemote = isRemote,
        IsCurrent = isCurrent
    };
}

public class StashListParserTests
{
    [Fact]
    public void ParsesPrettyFormatWithUnitSeparators()
    {
        var output = "stash@{0}\u001fabc123\u001fOn main: named-wip\nstash@{1}\u001fdef456\u001fWIP on feature: 1a2b3c4 first";

        var stashes = StashListParser.Parse(output);

        Assert.Equal(2, stashes.Count);
        Assert.Equal("stash@{0}", stashes[0].Selector);
        Assert.Equal("abc123", stashes[0].Sha);
        Assert.Equal("On main: named-wip", stashes[0].Message);
        Assert.Equal("main • named-wip", stashes[0].DisplayLabel);
        Assert.Equal("feature • 1a2b3c4 first", stashes[1].DisplayLabel);
    }

    [Fact]
    public void ParsesLiteralPercent1fFromStashPrettyFormat()
    {
        var output = "stash@{0}%1fface97843deadbeef%1fOn develop: hotfix";

        var stash = Assert.Single(StashListParser.Parse(output));

        Assert.Equal("stash@{0}", stash.Selector);
        Assert.Equal("face97843deadbeef", stash.Sha);
        Assert.Equal("On develop: hotfix", stash.Message);
        Assert.Equal("develop • hotfix", stash.DisplayLabel);
    }
}

public class ChangedLineMergerTests
{
    [Fact]
    public void AppliesOnlyIncomingLineEdits()
    {
        var result = ChangedLineMerger.Apply(
            "AAA\nBBB\nCCC\nDDD\nEEE\nFFF\nLOCAL\n",
            "AAA\nBBB\nCCC\nDDD\nEEE\nFFF\nGGG\n",
            "AAA\nSTASHED\nCCC\nDDD\nEEE\nFFF\nGGG\n");

        Assert.False(result.HasConflict);
        Assert.Equal("AAA\nSTASHED\nCCC\nDDD\nEEE\nFFF\nLOCAL\n", result.Text);
    }

    [Fact]
    public void KeepsCurrentLineWhenIncomingOnlyAddsNearby()
    {
        var result = ChangedLineMerger.Apply(
            "from main\n",
            "from other\n",
            "from other\nextra heading\n");

        Assert.False(result.HasConflict);
        Assert.Equal("from main\nextra heading\n", result.Text);
    }

    [Fact]
    public void SameLineTakesIncomingAndKeepsCurrentOnlyLines()
    {
        var result = ChangedLineMerger.Apply(
            "shared\nours\nshared\nLOCAL\n",
            "shared\nconflict\nshared\n",
            "shared\ntheirs\nshared\n");

        Assert.False(result.HasConflict);
        Assert.Equal("shared\ntheirs\nshared\nLOCAL\n", result.Text);
    }
}
