using GitTree.Core;

namespace GitTree.Core.Tests;

public class PullRequestUrlsTests
{
    [Theory]
    [InlineData("https://github.com/owner/repo.git", "GitHubCompatible", "https://github.com/owner/repo")]
    [InlineData("https://github.com/owner/repo", "GitHubCompatible", "https://github.com/owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "GitHubCompatible", "https://github.com/owner/repo")]
    [InlineData("ssh://git@github.com/owner/repo.git", "GitHubCompatible", "https://github.com/owner/repo")]
    [InlineData("https://user:token@github.com/owner/repo.git", "GitHubCompatible", "https://github.com/owner/repo")]
    [InlineData("https://gitlab.com/group/sub/repo.git", "GitLab", "https://gitlab.com/group/sub/repo")]
    [InlineData("git@gitlab.com:group/repo.git", "GitLab", "https://gitlab.com/group/repo")]
    [InlineData("https://gitlab.example.com/team/app.git", "GitLab", "https://gitlab.example.com/team/app")]
    [InlineData("https://bitbucket.org/acme/widgets.git", "BitbucketCloud", "https://bitbucket.org/acme/widgets")]
    [InlineData("git@bitbucket.org:acme/widgets.git", "BitbucketCloud", "https://bitbucket.org/acme/widgets")]
    [InlineData("https://bitbucket.example.com/scm/PROJ/repo.git", "BitbucketServer", "https://bitbucket.example.com/projects/PROJ/repos/repo")]
    [InlineData("https://dev.azure.com/contoso/Fabrikam/_git/Repo", "AzureDevOps", "https://dev.azure.com/contoso/Fabrikam/_git/Repo")]
    [InlineData("oscar.d@example.net:v3/contoso/Fabrikam/Repo", "AzureDevOps", "https://dev.azure.com/contoso/Fabrikam/_git/Repo")]
    [InlineData("ssh://git@ssh.dev.azure.com/v3/contoso/Fabrikam/Repo", "AzureDevOps", "https://dev.azure.com/contoso/Fabrikam/_git/Repo")]
    [InlineData("https://contoso.visualstudio.com/Fabrikam/_git/Repo", "AzureDevOps", "https://dev.azure.com/contoso/Fabrikam/_git/Repo")]
    [InlineData("https://codeberg.org/owner/repo.git", "GitHubCompatible", "https://codeberg.org/owner/repo")]
    [InlineData("http://git.internal:8080/owner/repo.git", "GitHubCompatible", "http://git.internal:8080/owner/repo")]
    public void TryParse_RecognizesHostedRemotes(string url, string provider, string webBase)
    {
        Assert.True(PullRequestUrls.TryParse(url, out var repo));
        Assert.Equal(Enum.Parse<PullRequestProvider>(provider), repo.Provider);
        Assert.Equal(webBase, repo.WebBaseUrl);
    }

    [Theory]
    [InlineData("")]
    [InlineData("D:\\git\\repo")]
    [InlineData("C:/git/repo")]
    [InlineData("/home/git/repo.git")]
    [InlineData("file:///tmp/repo.git")]
    public void TryParse_RejectsLocalRemotes(string url)
    {
        Assert.False(PullRequestUrls.TryParse(url, out _));
    }

    [Fact]
    public void Create_BuildsGitHubCompareUrl()
    {
        Assert.True(PullRequestUrls.TryParse("git@github.com:owner/repo.git", out var repo));
        Assert.Equal(
            "https://github.com/owner/repo/compare/main...feature/login?expand=1",
            PullRequestUrls.Create(repo, "feature/login", "main"));
    }

    [Fact]
    public void Create_OmitsDestinationWhenMissing()
    {
        Assert.True(PullRequestUrls.TryParse("https://github.com/owner/repo.git", out var repo));
        Assert.Equal(
            "https://github.com/owner/repo/compare/feature?expand=1",
            PullRequestUrls.Create(repo, "feature", null));
    }

    [Fact]
    public void Create_BuildsGitLabMergeRequestUrl()
    {
        Assert.True(PullRequestUrls.TryParse("https://gitlab.com/group/sub/repo.git", out var repo));
        Assert.Equal(
            "https://gitlab.com/group/sub/repo/-/merge_requests/new?merge_request%5Bsource_branch%5D=feature%2Fx&merge_request%5Btarget_branch%5D=main",
            PullRequestUrls.Create(repo, "feature/x", "main"));
    }

    [Fact]
    public void Create_BuildsBitbucketCloudUrl()
    {
        Assert.True(PullRequestUrls.TryParse("https://bitbucket.org/acme/widgets.git", out var repo));
        Assert.Equal(
            "https://bitbucket.org/acme/widgets/pull-requests/new?source=feature&dest=main",
            PullRequestUrls.Create(repo, "feature", "main"));
    }

    [Fact]
    public void Create_BuildsAzureDevOpsUrl()
    {
        Assert.True(PullRequestUrls.TryParse("oscar.d@example.net:v3/contoso/Fabrikam/Repo", out var repo));
        Assert.Equal(
            "https://dev.azure.com/contoso/Fabrikam/_git/Repo/pullrequestcreate?sourceRef=feature&targetRef=main",
            PullRequestUrls.Create(repo, "feature", "main"));
    }

    [Fact]
    public void SourceBranchName_StripsRemotePrefix()
    {
        var remotes = new[] { Remote("origin") };
        var local = Branch("feature/login", isRemote: false);
        var remote = Branch("origin/feature/login", isRemote: true);

        Assert.Equal("feature/login", PullRequestUrls.SourceBranchName(local, remotes));
        Assert.Equal("feature/login", PullRequestUrls.SourceBranchName(remote, remotes));
    }

    [Fact]
    public void SuggestRemote_PrefersUpstreamThenOrigin()
    {
        var origin = Remote("origin");
        var upstream = Remote("upstream");
        var remotes = new[] { upstream, origin };

        var tracked = Branch("feature", isRemote: false, upstream: "origin/feature");
        Assert.Equal("origin", PullRequestUrls.SuggestRemote(remotes, tracked)?.Name);

        var fromUpstream = Branch("upstream/main", isRemote: true);
        Assert.Equal("upstream", PullRequestUrls.SuggestRemote(remotes, fromUpstream)?.Name);

        var local = Branch("scratch", isRemote: false);
        Assert.Equal("origin", PullRequestUrls.SuggestRemote(remotes, local)?.Name);
    }

    [Fact]
    public void DestinationBranches_ExcludesSourceAndPrefersMain()
    {
        var remotes = new[] { Remote("origin") };
        var branches = new[]
        {
            Branch("feature/login", isRemote: false),
            Branch("develop", isRemote: false),
            Branch("origin/main", isRemote: true),
            Branch("origin/feature/login", isRemote: true)
        };

        var destinations = PullRequestUrls.DestinationBranches(branches, "feature/login", remotes);
        Assert.Equal(["develop", "main"], destinations);
        Assert.Equal("main", PullRequestUrls.SuggestDestination(destinations));
    }

    private static RemoteInfo Remote(string name)
        => new() { Name = name, FetchUrl = $"https://github.com/acme/{name}.git" };

    private static BranchRef Branch(string name, bool isRemote, string? upstream = null)
        => new()
        {
            Name = name,
            FullName = isRemote ? $"refs/remotes/{name}" : $"refs/heads/{name}",
            TipSha = "abc",
            IsRemote = isRemote,
            IsCurrent = false,
            Upstream = upstream
        };
}
