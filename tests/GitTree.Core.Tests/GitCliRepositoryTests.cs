using GitTree.Core;
using GitTree.Git.Cli;

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
