using System.Diagnostics;
using GitTree.Core;

namespace GitTree.Core.Tests;

public class BranchPushTests
{
    [Theory]
    [MemberData(nameof(BranchNames))]
    public void Validate_MatchesGitCheckRefFormat(string name, bool allowed)
    {
        Assert.Equal(allowed, BranchRefNames.Validate(name) is null);
        Assert.Equal(allowed, GitAllows(name));
    }

    public static IEnumerable<object[]> BranchNames()
    {
        yield return ["feature", true];
        yield return ["feature/foo", true];
        yield return ["release.1", true];
        yield return ["feature/-foo", true];
        yield return ["foo.lockbar", true];
        yield return ["head", true];
        yield return ["@", true];
        yield return ["", false];
        yield return ["   ", false];
        yield return ["foo bar", false];
        yield return ["foo..bar", false];
        yield return ["-feature", false];
        yield return ["HEAD", false];
        yield return ["foo.lock", false];
        yield return ["foo@{1}", false];
        yield return ["/foo", false];
        yield return ["foo/", false];
        yield return ["foo//bar", false];
        yield return [".foo", false];
        yield return ["foo.", false];
        yield return ["foo/.bar", false];
        yield return ["a.lock/b", false];
        yield return ["feature.", false];
    }

    [Fact]
    public void ParseConfig_ReadsRemoteAndMergeForNestedBranchNames()
    {
        var output = """
            branch.main.remote origin
            branch.main.merge refs/heads/published
            branch.feature/foo.remote upstream
            branch.feature/foo.merge refs/heads/teams/review
            branch.main.vscode-merge-base abcdef
            """;

        var configured = BranchPush.ParseConfig(output);

        Assert.Equal(new BranchPushTarget("origin", "published"), configured["main"]);
        Assert.Equal(new BranchPushTarget("upstream", "teams/review"), configured["feature/foo"]);
        Assert.Equal(2, configured.Count);
    }

    [Fact]
    public void Apply_FillsATargetThatHasNotBeenFetched()
    {
        var branches = new[]
        {
            Branch("main", isCurrent: true, upstream: null)
        };
        var configured = new Dictionary<string, BranchPushTarget>
        {
            ["main"] = new("origin", "published")
        };

        var applied = BranchPush.Apply(branches, configured, [Remote("origin")]);

        Assert.Equal("origin/published", applied[0].Upstream);
        Assert.Equal("origin/published", applied[0].PushTargetLabel);
    }

    [Fact]
    public void Apply_KeepsAheadBehindWhenTheTrackingRefExists()
    {
        var branches = new[]
        {
            Branch("main", isCurrent: true, upstream: "origin/main", ahead: 2)
        };
        var configured = new Dictionary<string, BranchPushTarget>
        {
            ["main"] = new("origin", "main")
        };

        var applied = BranchPush.Apply(branches, configured, [Remote("origin")]);

        Assert.Equal("origin/main", applied[0].Upstream);
        Assert.Null(applied[0].PushTargetLabel);
        Assert.Equal(2, applied[0].Ahead);
    }

    [Fact]
    public void DistinctLabel_UsesTheLongestMatchingRemoteName()
    {
        var remotes = new[] { Remote("origin"), Remote("origin/foo") };

        Assert.Null(BranchPush.DistinctLabel("main", "origin/foo/main", remotes));
        Assert.Equal(
            "origin/foo/main",
            BranchPush.DistinctLabel("main", "origin/foo/main", [Remote("origin")]));
    }

    [Fact]
    public void Arguments_UseTheConfiguredRefspec()
    {
        var target = new BranchPushTarget("origin", "published");

        Assert.Equal(["push", "origin", "feature:published"], BranchPush.Arguments(target, "feature"));
        Assert.Equal(["push", "-u", "origin", "HEAD"], BranchPush.Arguments(null, "feature"));
        Assert.Equal(["push", "-u", "origin", "HEAD"], BranchPush.Arguments(target, "HEAD"));
    }

    private static BranchRef Branch(string name, bool isCurrent, string? upstream, int ahead = 0) => new()
    {
        Name = name,
        FullName = "refs/heads/" + name,
        TipSha = "abc",
        IsRemote = false,
        IsCurrent = isCurrent,
        Upstream = upstream,
        Ahead = ahead,
        Behind = 0
    };

    private static RemoteInfo Remote(string name) => new()
    {
        Name = name,
        FetchUrl = "https://example.com/" + name + ".git"
    };

    private static bool GitAllows(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var psi = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("check-ref-format");
        psi.ArgumentList.Add("--branch");
        psi.ArgumentList.Add(name);
        using var proc = Process.Start(psi)!;
        proc.WaitForExit();
        return proc.ExitCode == 0;
    }
}
