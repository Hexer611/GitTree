using GitTree.Core;

namespace GitTree.Core.Tests;

public class GitClonePathsTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "repo")]
    [InlineData("https://github.com/owner/repo", "repo")]
    [InlineData("https://github.com/owner/repo/", "repo")]
    [InlineData("git@github.com:owner/repo.git", "repo")]
    [InlineData("ssh://git@github.com/owner/repo.git", "repo")]
    [InlineData("https://github.com/owner/repo.git?foo=1#readme", "repo")]
    [InlineData("", "")]
    public void FolderNameFromUrl_ParsesCommonRemotes(string url, string expected)
    {
        Assert.Equal(expected, GitClonePaths.FolderNameFromUrl(url));
    }

    [Fact]
    public void SuggestDestination_AppendsRepoName()
    {
        var parent = Path.Combine(Path.GetTempPath(), "gittree-clone-parent");
        var dest = GitClonePaths.SuggestDestination(parent, "https://github.com/acme/widgets.git");
        Assert.Equal(Path.Combine(parent, "widgets"), dest);
    }

    [Fact]
    public void DestinationTracksSuggestion_WhenPathsMatch()
    {
        var suggested = Path.Combine(Path.GetTempPath(), "widgets");
        Assert.True(GitClonePaths.DestinationTracksSuggestion(suggested, suggested));
        Assert.False(GitClonePaths.DestinationTracksSuggestion(Path.Combine(Path.GetTempPath(), "other"), suggested));
    }

    [Fact]
    public void NextDestination_KeepsRememberedParentWhenUrlIsEntered()
    {
        var parent = Path.Combine(Path.GetTempPath(), "clones");
        var next = GitClonePaths.NextDestination(
            parent,
            "https://github.com/acme/newgitclone.git",
            parent,
            parent);

        Assert.Equal(Path.Combine(parent, "newgitclone"), next);
    }

    [Fact]
    public void NextDestination_LeavesHandEditedPathAlone()
    {
        var parent = Path.Combine(Path.GetTempPath(), "clones");
        var custom = Path.Combine(Path.GetTempPath(), "elsewhere", "mine");
        var next = GitClonePaths.NextDestination(
            parent,
            "https://github.com/acme/newgitclone.git",
            custom,
            parent);

        Assert.Equal(custom, next);
    }

    [Fact]
    public void ResolveParent_UsesDestinationAsParentUntilRepoNameExists()
    {
        var parent = Path.Combine(Path.GetTempPath(), "clones");
        Assert.Equal(parent, GitClonePaths.ResolveParent(parent, ""));
        Assert.Equal(parent, GitClonePaths.ResolveParent(Path.Combine(parent, "newgitclone"), "https://github.com/acme/newgitclone.git"));
    }
}
