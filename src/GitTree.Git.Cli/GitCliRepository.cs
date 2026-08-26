using GitTree.Core;

namespace GitTree.Git.Cli;

public sealed class GitCliRepository : IGitRepository
{
    private readonly GitCliRunner _git;
    private readonly IGitHistoryReader? _historyReader;

    public GitCliRepository(string workingDirectory, IGitHistoryReader? historyReader = null)
    {
        WorkingDirectory = Path.GetFullPath(workingDirectory);
        _git = new GitCliRunner(WorkingDirectory);
        _historyReader = historyReader;
    }

    public string WorkingDirectory { get; }

    public async Task<RepositorySnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var statusTask = _git.RunAsync(["status", "--porcelain=v1", "-b", "-uall", "--untracked-files=all"], cancellationToken: cancellationToken);
        var headTask = _git.RunAsync(["rev-parse", "HEAD"], throwOnError: false, cancellationToken: cancellationToken);
        var branchTask = _git.RunAsync(["rev-parse", "--abbrev-ref", "HEAD"], throwOnError: false, cancellationToken: cancellationToken);
        var branchesTask = _git.RunAsync(["for-each-ref", "--format=%(refname)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)", "refs/heads", "refs/remotes"], throwOnError: false, cancellationToken: cancellationToken);
        var tagsTask = _git.RunAsync(["for-each-ref", "--format=%(refname:short)%1f%(objectname)", "refs/tags"], throwOnError: false, cancellationToken: cancellationToken);
        var remotesTask = _git.RunAsync(["remote", "-v"], throwOnError: false, cancellationToken: cancellationToken);
        var stashTask = _git.RunAsync(["stash", "list", "--format=%gd%1f%H%1f%s"], throwOnError: false, cancellationToken: cancellationToken);
        var worktreesTask = _git.RunAsync(["worktree", "list", "--porcelain"], throwOnError: false, cancellationToken: cancellationToken);
        var gitDir = GitDir.Resolve(WorkingDirectory);
        var mergeHead = File.Exists(Path.Combine(gitDir, "MERGE_HEAD"));
        var rebase = Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
                     || Directory.Exists(Path.Combine(gitDir, "rebase-apply"));

        await Task.WhenAll(statusTask, headTask, branchTask, branchesTask, tagsTask, remotesTask, stashTask, worktreesTask);

        var statusText = statusTask.Result;
        var changes = StatusPorcelainParser.Parse(statusText);
        var head = headTask.Result.Trim();
        var branch = branchTask.Result.Trim();
        var detached = branch is "HEAD" or "";

        IReadOnlyList<CommitNode> commits;
        try
        {
            if (_historyReader is not null)
            {
                commits = _historyReader.ReadCommits(WorkingDirectory);
            }
            else
            {
                commits = await ReadLogAsync(cancellationToken);
            }
        }
        catch
        {
            commits = await ReadLogAsync(cancellationToken);
        }

        return new RepositorySnapshot
        {
            WorkingDirectory = WorkingDirectory,
            HeadSha = head,
            CurrentBranch = detached ? $"detached {Truncate(head)}" : branch,
            IsDetached = detached,
            Operation = new OperationState
            {
                IsMerging = mergeHead,
                IsRebasing = rebase,
                ConflictMessage = mergeHead ? "Merge in progress" : rebase ? "Rebase in progress" : null
            },
            Changes = changes,
            Commits = commits,
            Branches = ParseBranches(branchesTask.Result),
            Tags = ParseTags(tagsTask.Result),
            Remotes = ParseRemotes(remotesTask.Result),
            Stashes = ParseStashes(stashTask.Result),
            Worktrees = WorktreeListParser.Parse(worktreesTask.Result, WorkingDirectory)
        };
    }

    private async Task<IReadOnlyList<CommitNode>> ReadLogAsync(CancellationToken cancellationToken)
    {
        var output = await _git.RunAsync(
            ["log", "--all", "--date-order", "-n", "400", "--pretty=format:%H%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%D%x1e"],
            throwOnError: false,
            cancellationToken: cancellationToken);
        return GitLogParser.Parse(output);
    }

    public Task StageAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        => RunPaths(["add", "--"], paths, cancellationToken);

    public Task UnstageAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
        => RunPaths(["restore", "--staged", "--"], paths, cancellationToken);

    public async Task DiscardAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        var list = paths.ToList();
        var tracked = new List<string>();
        var untracked = new List<string>();
        foreach (var path in list)
        {
            var full = Path.Combine(WorkingDirectory, path);
            var check = await _git.RunAsync(["ls-files", "--", path], throwOnError: false, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(check) && File.Exists(full))
                untracked.Add(path);
            else
                tracked.Add(path);
        }

        if (tracked.Count > 0)
            await RunPaths(["restore", "--worktree", "--source=HEAD", "--"], tracked, cancellationToken);

        foreach (var path in untracked)
        {
            var full = Path.Combine(WorkingDirectory, path);
            if (File.Exists(full))
                File.Delete(full);
        }
    }

    public Task CommitAsync(string message, bool amend = false, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "commit" };
        if (amend)
            args.Add("--amend");
        args.Add("-m");
        args.Add(message);
        return _git.RunAsync(args, cancellationToken: cancellationToken);
    }

    public Task CheckoutAsync(string refOrSha, CancellationToken cancellationToken = default)
        => _git.RunAsync(["checkout", refOrSha], cancellationToken: cancellationToken);

    public Task CreateBranchAsync(string name, string? startPoint = null, CancellationToken cancellationToken = default)
        => _git.RunAsync(startPoint is null ? ["checkout", "-b", name] : ["checkout", "-b", name, startPoint], cancellationToken: cancellationToken);

    public Task DeleteBranchAsync(string name, bool force = false, CancellationToken cancellationToken = default)
        => _git.RunAsync(["branch", force ? "-D" : "-d", name], cancellationToken: cancellationToken);

    public Task FetchAsync(string? remote = null, CancellationToken cancellationToken = default)
        => _git.RunAsync(string.IsNullOrWhiteSpace(remote) ? ["fetch", "--all", "--prune"] : ["fetch", "--prune", remote], cancellationToken: cancellationToken);

    public Task PullAsync(CancellationToken cancellationToken = default)
        => _git.RunAsync(["pull", "--no-edit"], throwOnError: true, cancellationToken: cancellationToken);

    public Task PushAsync(string? remote = null, string? branch = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(remote) && string.IsNullOrWhiteSpace(branch))
            return _git.RunAsync(["push", "-u", "origin", "HEAD"], cancellationToken: cancellationToken);
        if (string.IsNullOrWhiteSpace(branch))
            return _git.RunAsync(["push", remote!], cancellationToken: cancellationToken);
        return _git.RunAsync(["push", "-u", remote ?? "origin", branch], cancellationToken: cancellationToken);
    }

    public Task StashSaveAsync(string? message = null, CancellationToken cancellationToken = default)
        => _git.RunAsync(string.IsNullOrWhiteSpace(message)
            ? ["stash", "push", "-u"]
            : ["stash", "push", "-u", "-m", message], cancellationToken: cancellationToken);

    public Task StashApplyAsync(int index, CancellationToken cancellationToken = default)
        => _git.RunAsync(["stash", "apply", $"stash@{{{index}}}"], cancellationToken: cancellationToken);

    public Task StashDropAsync(int index, CancellationToken cancellationToken = default)
        => _git.RunAsync(["stash", "drop", $"stash@{{{index}}}"], cancellationToken: cancellationToken);

    public async Task ImportChangesFromWorktreeAsync(WorktreeInfo worktree, CancellationToken cancellationToken = default)
    {
        var other = Path.GetFullPath(worktree.Path);
        if (string.Equals(other, WorkingDirectory, StringComparison.OrdinalIgnoreCase))
            throw new GitException("worktree import", 1, "That worktree is already the current one.");

        GitException? mergeError = null;
        var mergeRef = worktree.MergeRef;
        if (!string.IsNullOrWhiteSpace(mergeRef))
        {
            try
            {
                await MergeAsync(mergeRef, cancellationToken);
            }
            catch (GitException ex)
            {
                mergeError = ex;
            }
        }

        var diff = await _git.RunAsync(
            ["diff", "HEAD"],
            throwOnError: false,
            cancellationToken: cancellationToken,
            workingDirectory: other);
        if (!string.IsNullOrWhiteSpace(diff))
        {
            await _git.RunWithInputAsync(
                ["apply", "--3way", "--whitespace=nowarn"],
                diff,
                throwOnError: true,
                cancellationToken);
        }

        var untracked = await _git.RunAsync(
            ["ls-files", "-o", "--exclude-standard"],
            throwOnError: false,
            cancellationToken: cancellationToken,
            workingDirectory: other);
        foreach (var relative in untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var source = Path.GetFullPath(Path.Combine(other, relative));
            var dest = Path.GetFullPath(Path.Combine(WorkingDirectory, relative));
            if (!dest.StartsWith(WorkingDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
        }

        if (mergeError is not null)
            throw mergeError;
    }

    public Task RemoveWorktreeAsync(WorktreeInfo worktree, CancellationToken cancellationToken = default)
    {
        if (!worktree.CanRemove)
            throw new GitException("worktree remove", 1, "The current or main worktree cannot be removed.");
        return _git.RunAsync(["worktree", "remove", "--force", worktree.Path], cancellationToken: cancellationToken);
    }

    public Task MergeAsync(string branch, CancellationToken cancellationToken = default)
        => _git.RunAsync(["merge", "--no-edit", branch], cancellationToken: cancellationToken);

    public Task RebaseAsync(string onto, CancellationToken cancellationToken = default)
        => _git.RunAsync(["rebase", onto], cancellationToken: cancellationToken);

    public Task ContinueMergeAsync(CancellationToken cancellationToken = default)
        => _git.RunWithEditorTrueAsync(["merge", "--continue"], cancellationToken);

    public Task AbortMergeAsync(CancellationToken cancellationToken = default)
        => _git.RunAsync(["merge", "--abort"], cancellationToken: cancellationToken);

    public Task ContinueRebaseAsync(CancellationToken cancellationToken = default)
        => _git.RunWithEditorTrueAsync(["rebase", "--continue"], cancellationToken);

    public Task AbortRebaseAsync(CancellationToken cancellationToken = default)
        => _git.RunAsync(["rebase", "--abort"], cancellationToken: cancellationToken);

    public async Task TakeOursAsync(string path, CancellationToken cancellationToken = default)
    {
        await _git.RunAsync(["checkout", "--ours", "--", path], cancellationToken: cancellationToken);
        await MarkResolvedAsync(path, cancellationToken);
    }

    public async Task TakeTheirsAsync(string path, CancellationToken cancellationToken = default)
    {
        await _git.RunAsync(["checkout", "--theirs", "--", path], cancellationToken: cancellationToken);
        await MarkResolvedAsync(path, cancellationToken);
    }

    public Task MarkResolvedAsync(string path, CancellationToken cancellationToken = default)
        => _git.RunAsync(["add", "--", path], cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<FileChange>> GetCommitFilesAsync(string sha, CancellationToken cancellationToken = default)
    {
        var output = await _git.RunAsync(
            ["diff-tree", "--no-commit-id", "-r", "-M", "--name-status", sha],
            throwOnError: false,
            cancellationToken: cancellationToken);
        var list = new List<FileChange>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 2)
                continue;
            var code = parts[0][0];
            var kind = StatusPorcelainParser.ParseStatusChar(code);
            string path;
            string? oldPath = null;
            if (parts.Length >= 3)
            {
                oldPath = parts[1];
                path = parts[2];
            }
            else
            {
                path = parts[1];
            }

            list.Add(new FileChange
            {
                Path = path.Replace('\\', '/'),
                OldPath = oldPath,
                IndexStatus = kind,
                WorkTreeStatus = FileChangeKind.Unmodified,
                IsConflict = false
            });
        }

        return list;
    }

    public async Task<string> GetDiffAsync(DiffRequest request, CancellationToken cancellationToken = default)
    {
        return request.Kind switch
        {
            DiffKind.Index => await _git.RunAsync(WithPath(["diff", "--cached"], request.Path), throwOnError: false, cancellationToken: cancellationToken),
            DiffKind.Commit when request.CommitSha is not null =>
                await _git.RunAsync(WithPath(["show", "--format=", request.CommitSha], request.Path), throwOnError: false, cancellationToken: cancellationToken),
            _ => await _git.RunAsync(WithPath(["diff"], request.Path), throwOnError: false, cancellationToken: cancellationToken)
        };
    }

    public Task<string> ReadWorkingFileAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = SafePath(path);
        return File.Exists(full) ? File.ReadAllTextAsync(full, cancellationToken) : Task.FromResult(string.Empty);
    }

    public Task WriteWorkingFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = SafePath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        return File.WriteAllTextAsync(full, content, cancellationToken);
    }

    public void Dispose()
    {
    }

    private string SafePath(string path)
    {
        var full = Path.GetFullPath(Path.Combine(WorkingDirectory, path));
        if (!full.StartsWith(WorkingDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path escapes the repository.");
        return full;
    }

    private async Task RunPaths(string[] prefix, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (list.Count == 0)
            return;
        var args = new List<string>(prefix);
        args.AddRange(list);
        await _git.RunAsync(args, cancellationToken: cancellationToken);
    }

    private static string[] WithPath(string[] args, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return args;
        return [..args, "--", path];
    }

    private static IReadOnlyList<BranchRef> ParseBranches(string output)
    {
        var list = new List<BranchRef>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\u001f');
            if (parts.Length < 2)
                continue;
            var full = parts[0];
            var sha = parts[1];
            var upstream = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null;
            var headMark = parts.Length > 3 ? parts[3] : "";
            var isRemote = full.StartsWith("refs/remotes/", StringComparison.Ordinal);
            var name = isRemote
                ? full["refs/remotes/".Length..]
                : full.StartsWith("refs/heads/", StringComparison.Ordinal)
                    ? full["refs/heads/".Length..]
                    : full;
            if (name.EndsWith("/HEAD", StringComparison.Ordinal))
                continue;
            list.Add(new BranchRef
            {
                Name = name,
                FullName = full,
                TipSha = sha,
                IsRemote = isRemote,
                IsCurrent = !isRemote && headMark.Contains('*'),
                Upstream = upstream
            });
        }

        return list;
    }

    private static IReadOnlyList<TagRef> ParseTags(string output)
    {
        var list = new List<TagRef>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\u001f');
            if (parts.Length < 2)
                continue;
            list.Add(new TagRef { Name = parts[0], TargetSha = parts[1] });
        }

        return list;
    }

    private static IReadOnlyList<RemoteInfo> ParseRemotes(string output)
    {
        var map = new Dictionary<string, RemoteInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;
            var name = parts[0];
            var rest = parts[1];
            var url = rest.Split(' ', 2)[0];
            if (rest.Contains("(fetch)", StringComparison.Ordinal) || !map.ContainsKey(name))
                map[name] = new RemoteInfo { Name = name, FetchUrl = url };
        }

        return map.Values.ToList();
    }

    private static IReadOnlyList<StashEntry> ParseStashes(string output)
    {
        var list = new List<StashEntry>();
        var index = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\u001f');
            var selector = parts.Length > 0 ? parts[0] : $"stash@{{{index}}}";
            var sha = parts.Length > 1 ? parts[1] : "";
            var message = parts.Length > 2 ? parts[2] : line;
            list.Add(new StashEntry { Index = index, Selector = selector, Sha = sha, Message = message });
            index++;
        }

        return list;
    }

    private static string Truncate(string sha) => sha.Length >= 7 ? sha[..7] : sha;
}
