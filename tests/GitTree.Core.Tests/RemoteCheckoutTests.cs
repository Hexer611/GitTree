using GitTree.Core;

namespace GitTree.Core.Tests;

public class RemoteCheckoutTests
{
    [Fact]
    public void LocalNameStripsRemotePrefix()
    {
        Assert.Equal("feature", RemoteCheckout.LocalNameFromRemote("origin/feature"));
        Assert.Equal("feature/deep", RemoteCheckout.LocalNameFromRemote("origin/feature/deep"));
        Assert.Equal("main", RemoteCheckout.LocalNameFromRemote("main"));
    }

    [Fact]
    public void CreatesLocalWhenNoneExists()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("main", "aaa1111", isCurrent: true),
            Branch("origin/feature", "bbb2222", isRemote: true)
        ]);

        Assert.Equal(RemoteCheckoutKind.CreateLocal, plan.Kind);
        Assert.Equal("feature", plan.LocalName);
        Assert.Equal("origin/feature", plan.RemoteRef);
    }

    [Fact]
    public void ChecksOutLocalWhenTipsMatch()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("feature", "aaa1111", isCurrent: true),
            Branch("origin/feature", "aaa1111", isRemote: true)
        ]);

        Assert.Equal(RemoteCheckoutKind.CheckoutLocal, plan.Kind);
        Assert.Equal("feature", plan.LocalName);
    }

    [Fact]
    public void ConflictsWhenLocalIsBehindRemote()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("feature", "aaa1111", upstream: "origin/feature", behind: 3),
            Branch("origin/feature", "ccc3333", isRemote: true)
        ]);

        Assert.Equal(RemoteCheckoutKind.Conflict, plan.Kind);
        Assert.Equal(RemoteCheckoutRelation.Behind, plan.Relation);
        Assert.Equal(3, plan.Behind);
        Assert.Contains("3 commits behind origin/feature", plan.Summary);

        var options = RemoteCheckout.ConflictOptions(plan);
        Assert.Equal(3, options.Count);
        Assert.Equal(RemoteCheckoutConflictAction.ReplaceLocal, options[0].Action);
        Assert.False(options[0].IsDestructive);
        Assert.Equal(RemoteCheckoutConflictAction.ReplaceLocal, RemoteCheckout.DefaultOption(plan, options).Action);
    }

    [Fact]
    public void ConflictsWhenLocalIsAheadOfRemote()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("feature", "ccc3333", upstream: "origin/feature", ahead: 2),
            Branch("origin/feature", "aaa1111", isRemote: true)
        ]);

        Assert.Equal(RemoteCheckoutKind.Conflict, plan.Kind);
        Assert.Equal(RemoteCheckoutRelation.Ahead, plan.Relation);

        var options = RemoteCheckout.ConflictOptions(plan);
        Assert.True(options[0].IsDestructive);
        Assert.Equal(RemoteCheckoutConflictAction.CheckoutLocal, RemoteCheckout.DefaultOption(plan, options).Action);
    }

    [Fact]
    public void ConflictsWhenLocalHasDiverged()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("feature", "bbb2222", upstream: "origin/feature", ahead: 1, behind: 4),
            Branch("origin/feature", "ccc3333", isRemote: true)
        ]);

        Assert.Equal(RemoteCheckoutRelation.Diverged, plan.Relation);
        Assert.Contains("1 ahead, 4 behind", plan.Summary);
        var options = RemoteCheckout.ConflictOptions(plan);
        Assert.Equal(RemoteCheckoutConflictAction.CheckoutLocal, RemoteCheckout.DefaultOption(plan, options).Action);
        Assert.True(options[0].IsDestructive);
    }

    [Fact]
    public void TreatsUntrackedMismatchAsDifferent()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("feature", "aaa1111"),
            Branch("origin/feature", "bbb2222", isRemote: true)
        ]);

        Assert.Equal(RemoteCheckoutKind.Conflict, plan.Kind);
        Assert.Equal(RemoteCheckoutRelation.Different, plan.Relation);
        Assert.Contains("different commit", plan.Summary);

        var options = RemoteCheckout.ConflictOptions(plan);
        Assert.Contains(options, o => o.Action == RemoteCheckoutConflictAction.DetachAtRemote);
        Assert.Equal(RemoteCheckoutConflictAction.CheckoutLocal, RemoteCheckout.DefaultOption(plan, options).Action);
    }

    [Fact]
    public void InfersBehindFromCommitGraphWhenUntracked()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("feature", "aaa1111"),
            Branch("origin/feature", "ccc3333", isRemote: true)
        ],
        [
            Commit("ccc3333", "bbb2222"),
            Commit("bbb2222", "aaa1111"),
            Commit("aaa1111")
        ]);

        Assert.Equal(RemoteCheckoutRelation.Behind, plan.Relation);
        Assert.Equal(2, plan.Behind);
        Assert.Contains("2 commits behind origin/feature", plan.Summary);
        var options = RemoteCheckout.ConflictOptions(plan);
        Assert.Equal(RemoteCheckoutConflictAction.ReplaceLocal, RemoteCheckout.DefaultOption(plan, options).Action);
        Assert.False(options[0].IsDestructive);
    }

    [Fact]
    public void InfersAheadFromCommitGraphWhenUntracked()
    {
        var plan = RemoteCheckout.For("origin/feature",
        [
            Branch("feature", "ccc3333"),
            Branch("origin/feature", "aaa1111", isRemote: true)
        ],
        [
            Commit("ccc3333", "aaa1111"),
            Commit("aaa1111")
        ]);

        Assert.Equal(RemoteCheckoutRelation.Ahead, plan.Relation);
        Assert.Equal(1, plan.Ahead);
        var options = RemoteCheckout.ConflictOptions(plan);
        Assert.True(options[0].IsDestructive);
        Assert.Equal(RemoteCheckoutConflictAction.CheckoutLocal, RemoteCheckout.DefaultOption(plan, options).Action);
    }

    private static CommitNode Commit(string sha, params string[] parents) => new()
    {
        Sha = sha,
        ParentShas = parents,
        AuthorName = "Test",
        AuthorEmail = "test@gittree.local",
        AuthorDate = DateTimeOffset.UnixEpoch,
        Subject = sha
    };

    private static BranchRef Branch(
        string name,
        string sha,
        bool isRemote = false,
        bool isCurrent = false,
        string? upstream = null,
        int ahead = 0,
        int behind = 0) => new()
    {
        Name = name,
        FullName = isRemote ? $"refs/remotes/{name}" : $"refs/heads/{name}",
        TipSha = sha,
        IsRemote = isRemote,
        IsCurrent = isCurrent,
        Upstream = upstream,
        Ahead = ahead,
        Behind = behind
    };
}
