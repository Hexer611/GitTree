using System.Diagnostics.CodeAnalysis;

namespace GitTree.Core;

public enum PullRequestProvider
{
    GitHubCompatible,
    GitLab,
    BitbucketCloud,
    BitbucketServer,
    AzureDevOps
}

public sealed class PullRequestHostedRepo
{
    public required PullRequestProvider Provider { get; init; }
    public required string WebBaseUrl { get; init; }
    public required string HostName { get; init; }
    public required string Path { get; init; }

    public string Display
    {
        get
        {
            const string https = "https://";
            const string http = "http://";
            if (WebBaseUrl.StartsWith(https, StringComparison.OrdinalIgnoreCase))
                return WebBaseUrl[https.Length..];
            if (WebBaseUrl.StartsWith(http, StringComparison.OrdinalIgnoreCase))
                return WebBaseUrl[http.Length..];
            return WebBaseUrl;
        }
    }
}

public static class PullRequestUrls
{
    private static readonly string[] PreferredDestinations =
        ["main", "master", "develop", "development", "trunk"];

    public static bool TryParse(string? fetchUrl, [NotNullWhen(true)] out PullRequestHostedRepo? repo)
    {
        repo = null;
        if (string.IsNullOrWhiteSpace(fetchUrl))
            return false;
        if (!TryGetLocation(fetchUrl, out var scheme, out var host, out var path))
            return false;

        host = host.Trim().TrimEnd('.');
        path = TrimGitSuffix(path).Trim('/');
        if (host.Length == 0 || path.Length == 0)
            return false;

        var hostKey = host.TrimStart('.').ToLowerInvariant();
        if (hostKey.StartsWith("www.", StringComparison.Ordinal))
            hostKey = hostKey[4..];

        if (TryAzureDevOps(hostKey, path, out repo))
            return true;

        if (hostKey.Equals("bitbucket.org", StringComparison.Ordinal))
        {
            if (!TryOwnerRepo(path, out var owner, out var name))
                return false;
            repo = Hosted(PullRequestProvider.BitbucketCloud, "https", "bitbucket.org", $"{owner}/{name}");
            return true;
        }

        if (IsBitbucketServer(hostKey, path, out var project, out var repoName))
        {
            repo = Hosted(PullRequestProvider.BitbucketServer, scheme, host, $"projects/{project}/repos/{repoName}");
            return true;
        }

        var provider = IsGitLab(hostKey, path)
            ? PullRequestProvider.GitLab
            : PullRequestProvider.GitHubCompatible;
        repo = Hosted(provider, scheme, host, path);
        return true;
    }

    public static string Create(PullRequestHostedRepo repo, string sourceBranch, string? destinationBranch)
    {
        var source = sourceBranch.Trim();
        var dest = destinationBranch?.Trim() ?? "";
        var hasDest = dest.Length > 0;

        return repo.Provider switch
        {
            PullRequestProvider.GitLab => GitLabUrl(repo.WebBaseUrl, source, hasDest ? dest : null),
            PullRequestProvider.BitbucketCloud => hasDest
                ? $"{repo.WebBaseUrl}/pull-requests/new?source={Uri.EscapeDataString(source)}&dest={Uri.EscapeDataString(dest)}"
                : $"{repo.WebBaseUrl}/pull-requests/new?source={Uri.EscapeDataString(source)}",
            PullRequestProvider.BitbucketServer => BitbucketServerUrl(repo.WebBaseUrl, source, hasDest ? dest : null),
            PullRequestProvider.AzureDevOps => hasDest
                ? $"{repo.WebBaseUrl}/pullrequestcreate?sourceRef={Uri.EscapeDataString(source)}&targetRef={Uri.EscapeDataString(dest)}"
                : $"{repo.WebBaseUrl}/pullrequestcreate?sourceRef={Uri.EscapeDataString(source)}",
            _ => hasDest
                ? $"{repo.WebBaseUrl}/compare/{EncodePath(dest)}...{EncodePath(source)}?expand=1"
                : $"{repo.WebBaseUrl}/compare/{EncodePath(source)}?expand=1"
        };
    }

    public static string SourceBranchName(BranchRef branch, IReadOnlyList<RemoteInfo> remotes)
        => branch.IsRemote ? SplitRemoteRef(branch.Name, remotes).Branch : branch.Name;

    public static RemoteInfo? SuggestRemote(IReadOnlyList<RemoteInfo> remotes, BranchRef source)
    {
        if (remotes.Count == 0)
            return null;

        string? preferred = null;
        if (source.IsRemote)
            preferred = SplitRemoteRef(source.Name, remotes).Remote;
        else if (!string.IsNullOrWhiteSpace(source.Upstream))
            preferred = SplitRemoteRef(source.Upstream, remotes).Remote;

        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = remotes.FirstOrDefault(r =>
                r.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return remotes.FirstOrDefault(r => r.Name.Equals("origin", StringComparison.OrdinalIgnoreCase))
               ?? remotes[0];
    }

    public static IReadOnlyList<string> DestinationBranches(
        IEnumerable<BranchRef> branches,
        string sourceBranch,
        IReadOnlyList<RemoteInfo> remotes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        void Add(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)
                || name.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
                || name.Equals(sourceBranch, StringComparison.OrdinalIgnoreCase)
                || !seen.Add(name))
                return;
            list.Add(name);
        }

        var ordered = branches
            .OrderBy(b => b.IsRemote ? 1 : 0)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var branch in ordered)
        {
            if (branch.IsRemote)
                Add(SplitRemoteRef(branch.Name, remotes).Branch);
            else
                Add(branch.Name);
        }

        return list;
    }

    public static string? SuggestDestination(IReadOnlyList<string> destinations)
    {
        foreach (var preferred in PreferredDestinations)
        {
            var match = destinations.FirstOrDefault(d =>
                d.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return destinations.Count > 0 ? destinations[0] : null;
    }

    public static (string Remote, string Branch) SplitRemoteRef(string name, IReadOnlyList<RemoteInfo> remotes)
    {
        foreach (var remote in remotes.OrderByDescending(r => r.Name.Length))
        {
            var prefix = remote.Name + "/";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && name.Length > prefix.Length)
                return (remote.Name, name[prefix.Length..]);
        }

        var slash = name.IndexOf('/');
        if (slash > 0 && slash < name.Length - 1)
            return (name[..slash], name[(slash + 1)..]);
        return ("", name);
    }

    private static PullRequestHostedRepo Hosted(
        PullRequestProvider provider,
        string scheme,
        string host,
        string path)
        => new()
        {
            Provider = provider,
            HostName = host,
            Path = path,
            WebBaseUrl = $"{scheme}://{host}/{path.Trim('/')}"
        };

    private static bool TryGetLocation(string fetchUrl, out string scheme, out string host, out string path)
    {
        scheme = "https";
        host = "";
        path = "";

        var value = StripQueryAndFragment(fetchUrl.Trim());
        if (value.Length == 0)
            return false;

        var schemeSep = value.IndexOf("://", StringComparison.Ordinal);
        if (schemeSep >= 0)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
                return false;

            host = uri.IdnHost.Length > 0 ? uri.IdnHost : uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
            scheme = WebScheme(uri);
            if (!uri.IsDefaultPort && IsHttpScheme(uri.Scheme))
                host = $"{host}:{uri.Port}";
            return host.Length > 0 && path.Length > 0;
        }

        if (LooksLikeWindowsPath(value))
            return false;

        var colon = value.LastIndexOf(':');
        if (colon <= 0)
            return false;

        var left = value[..colon];
        var right = value[(colon + 1)..];
        if (right.Length == 0 || right.StartsWith('\\'))
            return false;

        var at = left.LastIndexOf('@');
        host = at >= 0 ? left[(at + 1)..] : left;
        if (host.Length == 0 || host.Contains('/'))
            return false;

        path = right.TrimStart('/');
        return path.Length > 0;
    }

    private static bool TryAzureDevOps(string hostKey, string path, [NotNullWhen(true)] out PullRequestHostedRepo? repo)
    {
        repo = null;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return false;

        // Azure DevOps SSH: git@ssh.dev.azure.com:v3/{org}/{project}/{repo}
        if (parts.Length >= 4 && parts[0].Equals("v3", StringComparison.OrdinalIgnoreCase))
        {
            repo = AzureRepo(parts[1], parts[2], parts[3]);
            return true;
        }

        if (hostKey == "dev.azure.com" || hostKey.EndsWith(".dev.azure.com", StringComparison.Ordinal))
        {
            var git = IndexOfGit(parts);
            if (git >= 2 && git < parts.Length - 1)
            {
                repo = AzureRepo(parts[0], parts[git - 1], parts[git + 1]);
                return true;
            }
        }

        if (hostKey.EndsWith(".visualstudio.com", StringComparison.Ordinal)
            && !hostKey.Equals("vs-ssh.visualstudio.com", StringComparison.Ordinal))
        {
            var org = hostKey.Split('.')[0];
            var git = IndexOfGit(parts);
            if (git >= 1 && git < parts.Length - 1)
            {
                repo = AzureRepo(org, parts[git - 1], parts[git + 1]);
                return true;
            }
        }

        return false;
    }

    private static PullRequestHostedRepo AzureRepo(string org, string project, string name)
        => new()
        {
            Provider = PullRequestProvider.AzureDevOps,
            HostName = "dev.azure.com",
            Path = $"{org}/{project}/_git/{name}",
            WebBaseUrl = $"https://dev.azure.com/{org}/{project}/_git/{name}"
        };

    private static int IndexOfGit(string[] parts)
    {
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Equals("_git", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static bool IsBitbucketServer(string hostKey, string path, out string project, out string name)
    {
        project = "";
        name = "";
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4
            && parts[0].Equals("projects", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("repos", StringComparison.OrdinalIgnoreCase))
        {
            project = parts[1];
            name = parts[3];
            return project.Length > 0 && name.Length > 0;
        }

        if (parts.Length >= 3 && parts[0].Equals("scm", StringComparison.OrdinalIgnoreCase))
        {
            project = parts[1];
            name = parts[2];
            return project.Length > 0 && name.Length > 0;
        }

        if (hostKey.Contains("bitbucket", StringComparison.Ordinal)
            && !hostKey.Equals("bitbucket.org", StringComparison.Ordinal)
            && parts.Length >= 2)
        {
            project = parts[0];
            name = parts[1];
            return true;
        }

        return false;
    }

    private static bool IsGitLab(string hostKey, string path)
    {
        if (hostKey.Equals("gitlab.com", StringComparison.Ordinal) || hostKey.Contains("gitlab", StringComparison.Ordinal))
            return true;

        if (hostKey.Contains("github", StringComparison.Ordinal)
            || hostKey.Contains("gitea", StringComparison.Ordinal)
            || hostKey.Contains("forgejo", StringComparison.Ordinal)
            || hostKey.Equals("codeberg.org", StringComparison.Ordinal))
            return false;

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 2;
    }

    private static bool TryOwnerRepo(string path, out string owner, out string name)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            owner = "";
            name = "";
            return false;
        }

        owner = parts[0];
        name = parts[1];
        return true;
    }

    private static string GitLabUrl(string webBase, string source, string? dest)
    {
        var url = $"{webBase}/-/merge_requests/new?merge_request%5Bsource_branch%5D={Uri.EscapeDataString(source)}";
        if (!string.IsNullOrWhiteSpace(dest))
            url += $"&merge_request%5Btarget_branch%5D={Uri.EscapeDataString(dest)}";
        return url;
    }

    private static string BitbucketServerUrl(string webBase, string source, string? dest)
    {
        var url = $"{webBase}/pull-requests?create&sourceBranch={Uri.EscapeDataString("refs/heads/" + source)}";
        if (!string.IsNullOrWhiteSpace(dest))
            url += $"&targetBranch={Uri.EscapeDataString("refs/heads/" + dest)}";
        return url;
    }

    private static string EncodePath(string branch)
        => string.Join('/', branch.Split('/').Select(Uri.EscapeDataString));

    private static string WebScheme(Uri uri)
        => IsHttpScheme(uri.Scheme) ? uri.Scheme : "https";

    private static bool IsHttpScheme(string scheme)
        => scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
           || scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

    private static string StripQueryAndFragment(string value)
    {
        var hash = value.IndexOf('#');
        if (hash >= 0)
            value = value[..hash];
        var query = value.IndexOf('?');
        if (query >= 0)
            value = value[..query];
        return value;
    }

    private static string TrimGitSuffix(string path)
    {
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            return path[..^4];
        return path;
    }

    private static bool LooksLikeWindowsPath(string value)
        => value.Length >= 2 && char.IsLetter(value[0]) && value[1] == ':'
           && (value.Length == 2 || value[2] is '\\' or '/');
}
