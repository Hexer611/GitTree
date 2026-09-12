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
        var branchesTask = _git.RunAsync(["for-each-ref", "--format=%(refname)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)%1f%(upstream:track)", "refs/heads", "refs/remotes"], throwOnError: false, cancellationToken: cancellationToken);
        var tagsTask = _git.RunAsync(["for-each-ref", "--format=%(refname:short)%1f%(objectname)", "refs/tags"], throwOnError: false, cancellationToken: cancellationToken);
        var remotesTask = _git.RunAsync(["remote", "-v"], throwOnError: false, cancellationToken: cancellationToken);
        var stashTask = _git.RunAsync(["stash", "list", "--format=%gd%x1f%H%x1f%s"], throwOnError: false, cancellationToken: cancellationToken);
        var worktreesTask = _git.RunAsync(["worktree", "list", "--porcelain"], throwOnError: false, cancellationToken: cancellationToken);
        var gitDir = GitDir.Resolve(WorkingDirectory);
        var mergeHead = File.Exists(Path.Combine(gitDir, "MERGE_HEAD"));
        var rebase = Directory.Exists(Path.Combine(gitDir, "rebase-merge"))
                     || Directory.Exists(Path.Combine(gitDir, "rebase-apply"));
        var headFile = HeadRefParser.TryRead(gitDir);

        await Task.WhenAll(statusTask, headTask, branchTask, branchesTask, tagsTask, remotesTask, stashTask, worktreesTask);

        var statusText = statusTask.Result;
        var changes = await FlagWorkingTreeConflictsAsync(StatusPorcelainParser.Parse(statusText), cancellationToken);
        var head = headTask.Result.Trim();
        var branches = ParseBranches(branchesTask.Result);
        var currentFromRefs = branches.FirstOrDefault(b => b.IsCurrent && !b.IsRemote)?.Name;
        var headState = HeadRefParser.Resolve(headFile, branchTask.Result, head, currentFromRefs);

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

        HeadRefParser.ApplyToCommits(commits, headState, head);

        return new RepositorySnapshot
        {
            WorkingDirectory = WorkingDirectory,
            HeadSha = head,
            CurrentBranch = headState.CurrentBranch,
            IsDetached = headState.IsDetached,
            Operation = new OperationState
            {
                IsMerging = mergeHead,
                IsRebasing = rebase,
                ConflictMessage = mergeHead ? "Merge in progress" : rebase ? "Rebase in progress" : null
            },
            Changes = changes,
            Commits = commits,
            Branches = branches,
            Tags = ParseTags(tagsTask.Result),
            Remotes = ParseRemotes(remotesTask.Result),
            Stashes = StashListParser.Parse(stashTask.Result),
            Worktrees = WorktreeListParser.Parse(worktreesTask.Result, WorkingDirectory),
            Sync = SyncStatusParser.ParsePorcelain(statusText)
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

    public Task ApplyDiffPatchAsync(string patch, DiffPatchAction action, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "apply", "--unidiff-zero", "--ignore-whitespace", "--whitespace=nowarn" };
        switch (action)
        {
            case DiffPatchAction.Stage:
                args.Add("--cached");
                break;
            case DiffPatchAction.Unstage:
                args.Add("--cached");
                args.Add("--reverse");
                break;
            case DiffPatchAction.Discard:
                args.Add("--reverse");
                break;
        }

        args.Add("-");
        var text = patch.Replace("\r\n", "\n");
        if (!text.EndsWith('\n'))
            text += "\n";
        return _git.RunWithInputAsync(args, text, cancellationToken: cancellationToken);
    }

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

    public Task ResetAsync(string sha, ResetMode mode, CancellationToken cancellationToken = default)
    {
        var flag = mode switch
        {
            ResetMode.Soft => "--soft",
            ResetMode.Hard => "--hard",
            _ => "--mixed"
        };
        return _git.RunAsync(["reset", flag, sha], cancellationToken: cancellationToken);
    }

    public Task CherryPickAsync(string sha, CherryPickOptions? options = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "cherry-pick" };
        if (options?.NoCommit == true)
            args.Add("--no-commit");
        if (options?.IncludeCommitId == true)
            args.Add("-x");
        args.Add(sha);
        return _git.RunAsync(args, cancellationToken: cancellationToken);
    }

    public Task CreateBranchAsync(string name, string? startPoint = null, CancellationToken cancellationToken = default)
        => _git.RunAsync(startPoint is null ? ["checkout", "-b", name] : ["checkout", "-b", name, startPoint], cancellationToken: cancellationToken);

    public Task DeleteBranchAsync(string name, bool force = false, CancellationToken cancellationToken = default)
        => _git.RunAsync(["branch", force ? "-D" : "-d", name], cancellationToken: cancellationToken);

    public async Task<string> FetchAsync(string? remote = null, CancellationToken cancellationToken = default)
    {
        var output = await _git.RunCaptureAsync(
            string.IsNullOrWhiteSpace(remote) ? ["fetch", "--all", "--prune"] : ["fetch", "--prune", remote!],
            cancellationToken);
        return string.IsNullOrWhiteSpace(output) ? "Fetched. Already up to date with remotes." : output;
    }

    public async Task<string> PullAsync(CancellationToken cancellationToken = default)
    {
        var output = await _git.RunCaptureAsync(["pull", "--no-edit"], cancellationToken);
        return string.IsNullOrWhiteSpace(output) ? "Pulled. Already up to date." : output;
    }

    public async Task<string> PushAsync(string? remote = null, string? branch = null, CancellationToken cancellationToken = default)
    {
        string output;
        if (string.IsNullOrWhiteSpace(remote) && string.IsNullOrWhiteSpace(branch))
            output = await _git.RunCaptureAsync(["push", "-u", "origin", "HEAD"], cancellationToken);
        else if (string.IsNullOrWhiteSpace(branch))
            output = await _git.RunCaptureAsync(["push", remote!], cancellationToken);
        else
            output = await _git.RunCaptureAsync(["push", "-u", remote ?? "origin", branch], cancellationToken);
        return string.IsNullOrWhiteSpace(output) ? "Pushed." : output;
    }

    public Task StashSaveAsync(string? message = null, IReadOnlyList<string>? paths = null, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "stash", "push", "-u" };
        if (!string.IsNullOrWhiteSpace(message))
        {
            args.Add("-m");
            args.Add(message);
        }

        var selected = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (selected is { Count: > 0 })
        {
            args.Add("--");
            args.AddRange(selected);
        }

        return _git.RunAsync(args, cancellationToken: cancellationToken);
    }

    public async Task StashApplyAsync(int index, IReadOnlyList<string>? paths = null, CancellationToken cancellationToken = default)
    {
        var selector = StashSelector(index);
        var selected = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToList();
        var files = await GetStashFilesAsync(index, cancellationToken);
        var chosen = selected is not { Count: > 0 }
            ? files.ToList()
            : files.Where(f => selected.Contains(f.Path) || (f.OldPath is not null && selected.Contains(f.OldPath))).ToList();
        var tracked = chosen.Where(f => f.IndexStatus != FileChangeKind.Untracked).ToList();
        var untracked = chosen.Where(f => f.IndexStatus == FileChangeKind.Untracked).Select(f => f.Path).ToList();
        if (tracked.Count == 0 && untracked.Count == 0 && selected is { Count: > 0 })
            tracked = selected.Select(path => new FileChange
            {
                Path = path.Replace('\\', '/'),
                IndexStatus = FileChangeKind.Modified,
                WorkTreeStatus = FileChangeKind.Unmodified,
                IsConflict = false
            }).ToList();

        foreach (var file in tracked)
            await ApplyStashTrackedFileAsync(selector, file, cancellationToken);

        if (untracked.Count > 0 && await HasStashUntrackedAsync(selector, cancellationToken))
        {
            foreach (var path in untracked)
                await ApplyStashUntrackedFileAsync(selector, path, cancellationToken);
        }
    }

    public Task StashDropAsync(int index, CancellationToken cancellationToken = default)
        => _git.RunAsync(["stash", "drop", StashSelector(index)], cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<FileChange>> GetStashFilesAsync(int index, CancellationToken cancellationToken = default)
    {
        var selector = StashSelector(index);
        var output = await _git.RunAsync(
            ["stash", "show", "--name-status", selector],
            throwOnError: false,
            cancellationToken: cancellationToken);
        var files = ParseNameStatus(output).ToList();
        if (!await HasStashUntrackedAsync(selector, cancellationToken))
            return files;

        var untracked = await _git.RunAsync(
            ["ls-tree", "-r", "--name-only", $"{selector}^3"],
            throwOnError: false,
            cancellationToken: cancellationToken);
        var seen = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var line in untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var path = line.Replace('\\', '/');
            if (!seen.Add(path))
                continue;
            files.Add(new FileChange
            {
                Path = path,
                IndexStatus = FileChangeKind.Untracked,
                WorkTreeStatus = FileChangeKind.Untracked,
                IsConflict = false
            });
        }

        return files;
    }

    public async Task<WorktreeImportPreview> GetWorktreeImportPreviewAsync(WorktreeInfo worktree, CancellationToken cancellationToken = default)
    {
        var other = RequireOtherWorktree(worktree);
        var commits = await ListUniqueCommitsAsync(worktree, cancellationToken);
        var status = await _git.RunAsync(
            ["status", "--porcelain=v1", "-uall", "--untracked-files=all"],
            throwOnError: false,
            cancellationToken: cancellationToken,
            workingDirectory: other);
        return new WorktreeImportPreview
        {
            Worktree = worktree,
            Commits = commits,
            Files = StatusPorcelainParser.Parse(status)
        };
    }

    public async Task ImportChangesFromWorktreeAsync(WorktreeInfo worktree, WorktreeImportSelection? selection = null, CancellationToken cancellationToken = default)
    {
        var other = RequireOtherWorktree(worktree);
        var unique = await ListUniqueCommitsAsync(worktree, cancellationToken);
        var shouldMerge = (selection is null || selection.MergeBranch)
                          && unique.Count > 0
                          && !string.IsNullOrWhiteSpace(worktree.MergeRef);

        GitException? commitError = null;
        if (shouldMerge)
        {
            try
            {
                await _git.RunAsync(
                    ["merge", "--no-edit", worktree.MergeRef],
                    cancellationToken: cancellationToken);
            }
            catch (GitException ex)
            {
                commitError = ex;
            }
        }

        if (selection is null)
            await ApplyAllWorktreeFilesAsync(other, cancellationToken);
        else
            await ApplySelectedWorktreeFilesAsync(other, selection.FilePaths, cancellationToken);

        if (commitError is not null)
            throw commitError;
    }

    private string RequireOtherWorktree(WorktreeInfo worktree)
    {
        var other = Path.GetFullPath(worktree.Path);
        if (string.Equals(other, WorkingDirectory, StringComparison.OrdinalIgnoreCase))
            throw new GitException("worktree import", 1, "That worktree is already the current one.");
        return other;
    }

    private async Task<IReadOnlyList<WorktreeImportCommit>> ListUniqueCommitsAsync(WorktreeInfo worktree, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(worktree.HeadSha))
            return [];

        var output = await _git.RunAsync(
            ["log", "--reverse", "--pretty=format:%H%x1f%s", $"HEAD..{worktree.HeadSha}"],
            throwOnError: false,
            cancellationToken: cancellationToken);
        var list = new List<WorktreeImportCommit>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\u001f');
            if (parts.Length < 2 || parts[0].Length < 4)
                continue;
            list.Add(new WorktreeImportCommit { Sha = parts[0], Subject = parts[1] });
        }

        return list;
    }

    private async Task ApplyAllWorktreeFilesAsync(string other, CancellationToken cancellationToken)
    {
        var status = await ReadOtherStatusAsync(other, cancellationToken);
        await ImportTrackedFilesAsync(other, status, cancellationToken);
        await CopyUntrackedAsync(other, null, cancellationToken);
    }

    private async Task ApplySelectedWorktreeFilesAsync(string other, IReadOnlyList<string> filePaths, CancellationToken cancellationToken)
    {
        if (filePaths.Count == 0)
            return;

        var selected = filePaths
            .Select(p => p.Replace('\\', '/'))
            .Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var status = (await ReadOtherStatusAsync(other, cancellationToken))
            .Where(f => selected.Contains(f.Path))
            .ToList();
        var tracked = status.Where(f => f.IndexStatus != FileChangeKind.Untracked).ToList();
        await ImportTrackedFilesAsync(other, tracked, cancellationToken);
        await CopyUntrackedAsync(other, selected, cancellationToken);
    }

    private async Task<IReadOnlyList<FileChange>> ReadOtherStatusAsync(string other, CancellationToken cancellationToken)
    {
        var text = await _git.RunAsync(
            ["status", "--porcelain=v1", "-uall", "--untracked-files=all"],
            throwOnError: false,
            cancellationToken: cancellationToken,
            workingDirectory: other);
        return StatusPorcelainParser.Parse(text);
    }

    private async Task ImportTrackedFilesAsync(string other, IReadOnlyList<FileChange> files, CancellationToken cancellationToken)
    {
        var otherHead = (await _git.RunAsync(
            ["rev-parse", "HEAD"],
            throwOnError: false,
            cancellationToken: cancellationToken,
            workingDirectory: other)).Trim();
        if (string.IsNullOrWhiteSpace(otherHead))
            otherHead = null;

        foreach (var file in files)
        {
            if (file.IndexStatus == FileChangeKind.Untracked)
                continue;
            await ImportTrackedFileAsync(other, file, otherHead, cancellationToken);
        }
    }

    private async Task ImportTrackedFileAsync(
        string other,
        FileChange file,
        string? otherHead,
        CancellationToken cancellationToken)
    {
        var relative = file.Path.Replace('\\', '/');
        var source = CombineUnderRoot(other, relative);
        var deleted = file.IndexStatus == FileChangeKind.Deleted
                      || file.WorkTreeStatus == FileChangeKind.Deleted
                      || !File.Exists(source);
        var basePath = string.IsNullOrWhiteSpace(file.OldPath) ? relative : file.OldPath.Replace('\\', '/');
        var baseText = await ReadBlobAtAsync(otherHead, basePath, cancellationToken);
        await MergeIncomingOntoCurrentAsync(
            relative,
            deleted ? null : source,
            file.OldPath,
            baseText,
            deleted,
            cancellationToken);
    }

    private async Task ApplyStashTrackedFileAsync(string selector, FileChange file, CancellationToken cancellationToken)
    {
        var relative = file.Path.Replace('\\', '/');
        var basePath = string.IsNullOrWhiteSpace(file.OldPath) ? relative : file.OldPath.Replace('\\', '/');
        var deleted = file.IndexStatus == FileChangeKind.Deleted;
        var baseText = await ReadBlobAtAsync($"{selector}^1", basePath, cancellationToken);
        string? incomingTemp = null;
        try
        {
            if (!deleted)
            {
                incomingTemp = Path.GetTempFileName();
                await File.WriteAllTextAsync(
                    incomingTemp,
                    await ReadBlobAtAsync(selector, relative, cancellationToken),
                    cancellationToken);
            }

            await MergeIncomingOntoCurrentAsync(
                relative,
                incomingTemp,
                file.OldPath,
                baseText,
                deleted,
                cancellationToken);
        }
        finally
        {
            if (incomingTemp is not null)
            {
                try { File.Delete(incomingTemp); } catch { /* temp cleanup */ }
            }
        }
    }

    private async Task ApplyStashUntrackedFileAsync(string selector, string path, CancellationToken cancellationToken)
    {
        var relative = path.Replace('\\', '/');
        var incomingTemp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                incomingTemp,
                await ReadBlobAtAsync($"{selector}^3", relative, cancellationToken),
                cancellationToken);
            await MergeIncomingOntoCurrentAsync(relative, incomingTemp, null, "", incomingDeleted: false, cancellationToken);
        }
        finally
        {
            try { File.Delete(incomingTemp); } catch { /* temp cleanup */ }
        }
    }

    private async Task MergeIncomingOntoCurrentAsync(
        string relative,
        string? incomingPath,
        string? oldPath,
        string baseText,
        bool incomingDeleted,
        CancellationToken cancellationToken)
    {
        var dest = CombineUnderRoot(WorkingDirectory, relative);
        if (!string.IsNullOrWhiteSpace(oldPath))
        {
            var oldDest = CombineUnderRoot(WorkingDirectory, oldPath);
            if (File.Exists(oldDest) && !string.Equals(oldDest, dest, StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(dest))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Move(oldDest, dest);
                }
                else
                {
                    File.Delete(oldDest);
                }
            }
        }

        if (incomingDeleted)
        {
            if (!File.Exists(dest))
                return;
            var current = NormalizeNewlines(await File.ReadAllTextAsync(dest, cancellationToken));
            if (current == NormalizeNewlines(baseText))
                File.Delete(dest);
            return;
        }

        if (incomingPath is null || !File.Exists(incomingPath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (!File.Exists(dest))
        {
            File.Copy(incomingPath, dest, overwrite: true);
            return;
        }

        if (await FileHasConflictMarkersAsync(dest, cancellationToken))
            return;

        var destText = NormalizeNewlines(await File.ReadAllTextAsync(dest, cancellationToken));
        var incomingText = NormalizeNewlines(await File.ReadAllTextAsync(incomingPath, cancellationToken));
        if (destText == incomingText)
            return;

        var merged = ChangedLineMerger.Apply(destText, NormalizeNewlines(baseText), incomingText);
        await File.WriteAllTextAsync(dest, merged.Text, cancellationToken);
        if (!merged.HasConflict)
            return;

        var baseTemp = Path.GetTempFileName();
        var oursTemp = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(baseTemp, baseText, cancellationToken);
            await File.WriteAllTextAsync(oursTemp, destText, cancellationToken);
            await RecordUnmergedIndexAsync(relative, baseTemp, oursTemp, incomingPath, cancellationToken);
        }
        finally
        {
            try { File.Delete(baseTemp); } catch { /* temp cleanup */ }
            try { File.Delete(oursTemp); } catch { /* temp cleanup */ }
        }
    }

    private async Task<string> ReadBlobAtAsync(string? sha, string relative, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sha))
            return "";

        return await _git.RunAsync(
            ["show", $"{sha}:{relative}"],
            throwOnError: false,
            cancellationToken: cancellationToken);
    }

    private async Task<bool> IsUnmergedPathAsync(string relative, CancellationToken cancellationToken)
    {
        var output = await _git.RunAsync(
            ["ls-files", "-u", "--", relative],
            throwOnError: false,
            cancellationToken: cancellationToken);
        return output.Trim().Length > 0;
    }

    private async Task RecordUnmergedIndexAsync(
        string relative,
        string baseFile,
        string oursFile,
        string theirsFile,
        CancellationToken cancellationToken)
    {
        var mode = await ReadIndexModeAsync(relative, cancellationToken);
        var baseSha = (await _git.RunAsync(["hash-object", "-w", baseFile], cancellationToken: cancellationToken)).Trim();
        var oursSha = (await _git.RunAsync(["hash-object", "-w", oursFile], cancellationToken: cancellationToken)).Trim();
        var theirsSha = (await _git.RunAsync(["hash-object", "-w", theirsFile], cancellationToken: cancellationToken)).Trim();
        var info =
            $"0 0000000000000000000000000000000000000000\t{relative}\n" +
            $"{mode} {baseSha} 1\t{relative}\n" +
            $"{mode} {oursSha} 2\t{relative}\n" +
            $"{mode} {theirsSha} 3\t{relative}\n";
        await _git.RunWithInputAsync(["update-index", "--index-info"], info, throwOnError: false, cancellationToken);
    }

    private async Task<string> ReadIndexModeAsync(string relative, CancellationToken cancellationToken)
    {
        var output = await _git.RunAsync(["ls-files", "-s", "--", relative], throwOnError: false, cancellationToken: cancellationToken);
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (line is null)
            return "100644";
        var space = line.IndexOf(' ');
        return space == 6 ? line[..space] : "100644";
    }

    private async Task<IReadOnlyList<FileChange>> FlagWorkingTreeConflictsAsync(
        IReadOnlyList<FileChange> changes,
        CancellationToken cancellationToken)
    {
        var list = new List<FileChange>(changes.Count);
        foreach (var change in changes)
        {
            if (change.IsConflict || change.IndexStatus == FileChangeKind.Untracked)
            {
                list.Add(change);
                continue;
            }

            var full = CombineUnderRoot(WorkingDirectory, change.Path);
            if (await FileHasConflictMarkersAsync(full, cancellationToken))
                list.Add(change.WithConflict());
            else
                list.Add(change);
        }

        return list;
    }

    private static async Task<bool> FileHasConflictMarkersAsync(string fullPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(fullPath))
            return false;
        await using var stream = File.OpenRead(fullPath);
        var length = (int)Math.Min(stream.Length, 512 * 1024);
        var buffer = new byte[length];
        var read = await stream.ReadAsync(buffer.AsMemory(0, length), cancellationToken);
        var text = System.Text.Encoding.UTF8.GetString(buffer.AsSpan(0, read));
        return text.Contains("<<<"+"<"+"<<<") && text.Contains(">>>"+">"+">>>");
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n");

    private static string CombineUnderRoot(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new GitException("worktree import", 1, "Path escapes the repository.");
        return full;
    }

    private async Task<List<string>> ListUntrackedAsync(string other, CancellationToken cancellationToken)
    {
        var untracked = await _git.RunAsync(
            ["ls-files", "-o", "--exclude-standard"],
            throwOnError: false,
            cancellationToken: cancellationToken,
            workingDirectory: other);
        return untracked
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Replace('\\', '/'))
            .ToList();
    }

    private async Task CopyUntrackedAsync(string other, IReadOnlyList<string>? onlyPaths, CancellationToken cancellationToken)
    {
        var relatives = await ListUntrackedAsync(other, cancellationToken);
        IEnumerable<string> toCopy = relatives;
        if (onlyPaths is not null)
        {
            var allow = onlyPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            toCopy = relatives.Where(allow.Contains);
        }

        foreach (var relative in toCopy)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = CombineUnderRoot(other, relative);
            if (!File.Exists(source))
                continue;
            await MergeIncomingOntoCurrentAsync(relative, source, null, "", incomingDeleted: false, cancellationToken);
        }
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
        return ParseNameStatus(output);
    }

    public async Task<string> GetDiffAsync(DiffRequest request, CancellationToken cancellationToken = default)
    {
        return request.Kind switch
        {
            DiffKind.Index => await _git.RunAsync(WithPath(["diff", "--cached"], request.Path), throwOnError: false, cancellationToken: cancellationToken),
            DiffKind.Commit when request.CommitSha is not null =>
                await _git.RunAsync(WithPath(["show", "--format=", request.CommitSha], request.Path), throwOnError: false, cancellationToken: cancellationToken),
            DiffKind.Stash when request.StashIndex is int stashIndex =>
                await GetStashDiffAsync(stashIndex, request.Path, cancellationToken),
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

    private static string[] WithPath(IReadOnlyList<string> args, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return args as string[] ?? args.ToArray();
        return [..args, "--", path];
    }

    private static string StashSelector(int index) => $"stash@{{{index}}}";

    private async Task<string> GetStashDiffAsync(int index, string? path, CancellationToken cancellationToken)
    {
        var selector = StashSelector(index);
        if (string.IsNullOrWhiteSpace(path))
        {
            return await _git.RunAsync(
                ["stash", "show", "-p", "--include-untracked", selector],
                throwOnError: false,
                cancellationToken: cancellationToken);
        }

        var tracked = await _git.RunAsync(
            ["diff", $"{selector}^1", selector, "--", path],
            throwOnError: false,
            cancellationToken: cancellationToken);
        if (!string.IsNullOrWhiteSpace(tracked))
            return tracked;

        var untracked = await _git.RunAsync(
            ["show", $"{selector}^3:{path.Replace('\\', '/')}"],
            throwOnError: false,
            cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(untracked) ? "" : FormatNewFileDiff(path, untracked);
    }

    private static string FormatNewFileDiff(string path, string content)
    {
        var normalized = path.Replace('\\', '/');
        var lines = content.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0)
            lines = lines[..^1];
        var body = string.Join('\n', lines.Select(l => "+" + l));
        return $"diff --git a/{normalized} b/{normalized}\nnew file mode 100644\n--- /dev/null\n+++ b/{normalized}\n@@ -0,0 +1,{lines.Length} @@\n{body}\n";
    }

    private async Task<bool> HasStashUntrackedAsync(string selector, CancellationToken cancellationToken)
    {
        var sha = await _git.RunAsync(
            ["rev-parse", "--verify", "--quiet", $"{selector}^3"],
            throwOnError: false,
            cancellationToken: cancellationToken);
        return !string.IsNullOrWhiteSpace(sha);
    }

    private static IReadOnlyList<FileChange> ParseNameStatus(string output)
    {
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
            var track = parts.Length > 4 ? parts[4] : "";
            var (ahead, behind) = SyncStatusParser.ParseTrack(track);
            list.Add(new BranchRef
            {
                Name = name,
                FullName = full,
                TipSha = sha,
                IsRemote = isRemote,
                IsCurrent = !isRemote && headMark.Contains('*'),
                Upstream = upstream,
                Ahead = ahead,
                Behind = behind
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

}

