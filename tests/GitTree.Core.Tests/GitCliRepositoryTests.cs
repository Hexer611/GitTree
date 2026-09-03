using GitTree.Core;
using GitTree.Git.Cli;
using GitTree.Git.LibGit2;

namespace GitTree.Core.Tests;

public class GitCliRepositoryTests
{
    [Fact]
    public async Task StageCommitAndStatusRoundTrip()
    {
        var root = CreateTempRepo();
        try
        {
            var file = Path.Combine(root, "readme.txt");
            await File.WriteAllTextAsync(file, "hello\n");

            using var repo = new GitCliRepository(root);
            var before = await repo.RefreshAsync();
            Assert.Contains(before.Changes, c => c.Path == "readme.txt" && c.IndexStatus == FileChangeKind.Untracked);

            await repo.StageAsync(["readme.txt"]);
            await repo.CommitAsync("add readme");

            var after = await repo.RefreshAsync();
            Assert.DoesNotContain(after.Changes, c => c.Path == "readme.txt");
            Assert.Contains(after.Commits, c => c.Subject == "add readme");
            Assert.False(after.IsDetached);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task BranchStashAndDiff()
    {
        var root = CreateTempRepo();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("first");

            await repo.CreateBranchAsync("feature");
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "two\n");
            await repo.StashSaveAsync("wip");

            var snap = await repo.RefreshAsync();
            Assert.Contains(snap.Branches, b => !b.IsRemote && b.Name == "feature" && b.IsCurrent);
            Assert.NotEmpty(snap.Stashes);

            await repo.StashApplyAsync(0);
            var diff = await repo.GetDiffAsync(new DiffRequest { Kind = DiffKind.WorkTree, Path = "a.txt" });
            Assert.Contains("-one", diff);
            Assert.Contains("+two", diff);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ListsWorktreesAndImportsCommitsAndUntracked()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "base\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            await File.WriteAllTextAsync(Path.Combine(wt, "a.txt"), "other\n");
            Run(wt, "add", "a.txt");
            Run(wt, "commit", "-m", "other change");
            await File.WriteAllTextAsync(Path.Combine(wt, "extra.txt"), "extra\n");

            var snap = await repo.RefreshAsync();
            Assert.True(snap.Worktrees.Count >= 2);
            Assert.Contains(snap.Worktrees, w => w.IsCurrent);
            var other = snap.Worktrees.Single(w => !w.IsCurrent);
            Assert.Equal("other", other.Branch);

            var preview = await repo.GetWorktreeImportPreviewAsync(other);
            Assert.Contains(preview.Commits, c => c.Subject == "other change");
            Assert.Contains(preview.Files, f => f.Path == "extra.txt");

            await repo.ImportChangesFromWorktreeAsync(other);

            Assert.True(other.CanRemove);
            Assert.False(snap.Worktrees.Single(w => w.IsCurrent).CanRemove);
            await repo.RemoveWorktreeAsync(other);
            var remaining = await repo.RefreshAsync();
            Assert.DoesNotContain(remaining.Worktrees, w => string.Equals(w.Path, other.Path, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ImportsOnlySelectedWorktreeFiles()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "base\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            await File.WriteAllTextAsync(Path.Combine(wt, "a.txt"), "other\n");
            Run(wt, "add", "a.txt");
            Run(wt, "commit", "-m", "other change");
            await File.WriteAllTextAsync(Path.Combine(wt, "keep.txt"), "keep\n");
            await File.WriteAllTextAsync(Path.Combine(wt, "skip.txt"), "skip\n");

            var snap = await repo.RefreshAsync();
            var other = snap.Worktrees.Single(w => !w.IsCurrent);
            await repo.ImportChangesFromWorktreeAsync(other, new WorktreeImportSelection
            {
                FilePaths = ["keep.txt"]
            });

            var after = await repo.RefreshAsync();
            Assert.True(File.Exists(Path.Combine(root, "keep.txt")));
            Assert.False(File.Exists(Path.Combine(root, "skip.txt")));
            Assert.Equal("base\n", (await File.ReadAllTextAsync(Path.Combine(root, "a.txt"))).Replace("\r\n", "\n"));
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ImportsMarkdownAndJsonWhenBasesDiverge()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "README.md"), "from main\n");
            await File.WriteAllTextAsync(Path.Combine(root, "config.json"), """{"name":"main"}""" + "\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["README.md", "config.json"]);
            await repo.CommitAsync("main files");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            await File.WriteAllTextAsync(Path.Combine(wt, "README.md"), "from other\n");
            await File.WriteAllTextAsync(Path.Combine(wt, "config.json"), """{"name":"other"}""" + "\n");
            Run(wt, "add", "README.md", "config.json");
            Run(wt, "commit", "-m", "other files");
            await File.WriteAllTextAsync(Path.Combine(wt, "README.md"), "from other\nextra heading\n");
            await File.WriteAllTextAsync(Path.Combine(wt, "config.json"), """{"name":"other","ok":true}""" + "\n");

            var snap = await repo.RefreshAsync();
            var other = snap.Worktrees.Single(w => !w.IsCurrent);
            await repo.ImportChangesFromWorktreeAsync(other, new WorktreeImportSelection
            {
                FilePaths = ["README.md", "config.json"]
            });

            var after = await repo.RefreshAsync();
            Assert.DoesNotContain(after.Changes, c => c.IsConflict);

            var readme = (await File.ReadAllTextAsync(Path.Combine(root, "README.md"))).Replace("\r\n", "\n");
            var json = (await File.ReadAllTextAsync(Path.Combine(root, "config.json"))).Replace("\r\n", "\n");
            Assert.Equal("from other\nextra heading\n", readme);
            Assert.Equal("""{"name":"other","ok":true}""" + "\n", json);
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ImportsDifferentHunksWithoutConflict()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "AAA\nBBB\nCCC\nDDD\nEEE\nFFF\nGGG\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "CURRENT\nBBB\nCCC\nDDD\nEEE\nFFF\nGGG\n");
            await File.WriteAllTextAsync(Path.Combine(wt, "a.txt"), "AAA\nBBB\nCCC\nDDD\nEEE\nFFF\nOTHER\n");

            var snap = await repo.RefreshAsync();
            var other = snap.Worktrees.Single(w => !w.IsCurrent);
            await repo.ImportChangesFromWorktreeAsync(other, new WorktreeImportSelection
            {
                FilePaths = ["a.txt"]
            });

            var after = await repo.RefreshAsync();
            Assert.DoesNotContain(after.Changes, c => c.IsConflict);
            var text = (await File.ReadAllTextAsync(Path.Combine(root, "a.txt"))).Replace("\r\n", "\n");
            Assert.Equal("CURRENT\nBBB\nCCC\nDDD\nEEE\nFFF\nOTHER\n", text);
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ImportsSameHunkPrefersWorktreeVersion()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "shared\nconflict\nshared\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "shared\nours\nshared\n");
            await File.WriteAllTextAsync(Path.Combine(wt, "a.txt"), "shared\ntheirs\nshared\n");

            var snap = await repo.RefreshAsync();
            var other = snap.Worktrees.Single(w => !w.IsCurrent);
            await repo.ImportChangesFromWorktreeAsync(other, new WorktreeImportSelection
            {
                FilePaths = ["a.txt"]
            });

            var after = await repo.RefreshAsync();
            Assert.DoesNotContain(after.Changes, c => c.IsConflict);
            var text = (await File.ReadAllTextAsync(Path.Combine(root, "a.txt"))).Replace("\r\n", "\n");
            Assert.Equal("shared\ntheirs\nshared\n", text);
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ImportMergePrefersIncomingOnOverlappingCommits()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "base\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["f.txt"]);
            await repo.CommitAsync("base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            await File.WriteAllTextAsync(Path.Combine(wt, "f.txt"), "theirs\n");
            Run(wt, "add", "f.txt");
            Run(wt, "commit", "-m", "theirs");

            await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "ours\n");
            await repo.StageAsync(["f.txt"]);
            await repo.CommitAsync("ours");

            var snap = await repo.RefreshAsync();
            var other = snap.Worktrees.Single(w => !w.IsCurrent);
            await repo.ImportChangesFromWorktreeAsync(other, new WorktreeImportSelection
            {
                MergeBranch = true
            });

            var after = await repo.RefreshAsync();
            Assert.False(after.Operation.IsMerging);
            Assert.DoesNotContain(after.Changes, c => c.IsConflict);
            Assert.Equal("theirs\n", (await File.ReadAllTextAsync(Path.Combine(root, "f.txt"))).Replace("\r\n", "\n"));
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ResetSoftKeepsStagedAndWorktreeChanges()
    {
        var root = CreateTempRepo();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("first");
            var first = (await repo.RefreshAsync()).HeadSha;

            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "two\n");
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("second");
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "three\n");
            await repo.StageAsync(["a.txt"]);

            await repo.ResetAsync(first, ResetMode.Soft);
            var after = await repo.RefreshAsync();
            Assert.Equal(first, after.HeadSha);
            Assert.False(after.IsDetached);
            Assert.Equal("main", after.CurrentBranch);
            Assert.Contains(after.Changes, c => c.Path == "a.txt" && c.IsStaged);
            Assert.Equal("three\n", (await File.ReadAllTextAsync(Path.Combine(root, "a.txt"))).Replace("\r\n", "\n"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ResetMixedKeepsWorktreeAndClearsIndex()
    {
        var root = CreateTempRepo();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("first");
            var first = (await repo.RefreshAsync()).HeadSha;

            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "two\n");
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("second");
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "three\n");
            await repo.StageAsync(["a.txt"]);

            await repo.ResetAsync(first, ResetMode.Mixed);
            var after = await repo.RefreshAsync();
            Assert.Equal(first, after.HeadSha);
            Assert.Contains(after.Changes, c => c.Path == "a.txt" && c.IsUnstaged && !c.IsStaged);
            Assert.Equal("three\n", (await File.ReadAllTextAsync(Path.Combine(root, "a.txt"))).Replace("\r\n", "\n"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task ResetHardDiscardsWorktreeChanges()
    {
        var root = CreateTempRepo();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("first");
            var first = (await repo.RefreshAsync()).HeadSha;

            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "two\n");
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("second");
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "three\n");

            await repo.ResetAsync(first, ResetMode.Hard);
            var after = await repo.RefreshAsync();
            Assert.Equal(first, after.HeadSha);
            Assert.DoesNotContain(after.Changes, c => c.Path == "a.txt");
            Assert.Equal("one\n", (await File.ReadAllTextAsync(Path.Combine(root, "a.txt"))).Replace("\r\n", "\n"));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task UnbornBranchIsNotDetached()
    {
        var root = CreateTempRepo();
        try
        {
            using var repo = new GitCliRepository(root);
            var snap = await repo.RefreshAsync();
            Assert.False(snap.IsDetached);
            Assert.Equal("main", snap.CurrentBranch);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CheckoutCommitIsDetachedThenBranchCheckoutRecovers()
    {
        var root = CreateTempRepo();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("first");
            var sha = (await repo.RefreshAsync()).HeadSha;

            await repo.CheckoutAsync(sha);
            var detached = await repo.RefreshAsync();
            Assert.True(detached.IsDetached);
            Assert.StartsWith("detached ", detached.CurrentBranch);
            var headCommit = Assert.Single(detached.Commits, c => c.IsHead);
            Assert.Contains("HEAD", headCommit.Decorations);

            await repo.CheckoutAsync("main");
            var attached = await repo.RefreshAsync();
            Assert.False(attached.IsDetached);
            Assert.Equal("main", attached.CurrentBranch);
            var current = Assert.Single(attached.Commits, c => c.IsHead);
            Assert.Contains("main", current.Decorations);
            Assert.DoesNotContain("HEAD", current.Decorations);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LibGit2HistoryDoesNotMarkAttachedHeadAsDetached()
    {
        var root = CreateTempRepo();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one\n");
            using var repo = new GitCliRepository(root, new LibGit2HistoryReader());
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("first");

            var snap = await repo.RefreshAsync();
            Assert.False(snap.IsDetached);
            Assert.Equal("main", snap.CurrentBranch);
            var current = Assert.Single(snap.Commits, c => c.IsHead);
            Assert.Contains("main", current.Decorations);
            Assert.DoesNotContain("HEAD", current.Decorations);
            Assert.DoesNotContain(current.Decorations, d => d.StartsWith("HEAD ->", StringComparison.Ordinal));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task CheckoutRemoteTrackingRefCreatesLocalBranch()
    {
        var root = CreateTempRepo();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "one\n");
            using var repo = new GitCliRepository(root);
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("first");
            var first = (await repo.RefreshAsync()).HeadSha;
            Run(root, "branch", "other");
            await File.WriteAllTextAsync(Path.Combine(root, "a.txt"), "two\n");
            await repo.StageAsync(["a.txt"]);
            await repo.CommitAsync("second");
            Run(root, "update-ref", "refs/remotes/origin/feature", first);

            var snap = await repo.RefreshAsync();
            var choices = CheckoutTargets.ForCommit(first, snap.Branches);
            var remote = Assert.Single(choices, c => c.IsRemote);
            Assert.Equal("origin/feature", remote.RefOrSha);
            Assert.Equal("feature", remote.LocalName);

            await repo.CreateBranchAsync(remote.LocalName, remote.RefOrSha);
            var after = await repo.RefreshAsync();
            Assert.False(after.IsDetached);
            Assert.Equal("feature", after.CurrentBranch);
            Assert.Equal(first, after.HeadSha);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static string CreateTempRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Run(root, "init", "-b", "main");
        Run(root, "config", "user.email", "test@gittree.local");
        Run(root, "config", "user.name", "GitTree Tests");
        return root;
    }

    private static void Run(string dir, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(proc.StandardError.ReadToEnd());
    }

    private static void TryDelete(string root)
    {
        try
        {
            foreach (var info in new DirectoryInfo(root).GetFileSystemInfos("*", SearchOption.AllDirectories))
                info.Attributes = FileAttributes.Normal;
            Directory.Delete(root, true);
        }
        catch
        {
            // temp cleanup is best-effort on Windows
        }
    }
}
