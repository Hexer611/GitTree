using GitTree.Git.Cli;

namespace GitTree.Core.Tests;

public class MergeConflictTests
{
    [Fact]
    public async Task MergeConflictThenTakeOursAndContinue()
    {
        var root = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Run(root, "init", "-b", "main");
            Run(root, "config", "user.email", "test@gittree.local");
            Run(root, "config", "user.name", "GitTree Tests");
            await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "base\n");
            Run(root, "add", "f.txt");
            Run(root, "commit", "-m", "base");
            Run(root, "checkout", "-b", "theirs");
            await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "theirs\n");
            Run(root, "commit", "-am", "theirs");
            Run(root, "checkout", "main");
            await File.WriteAllTextAsync(Path.Combine(root, "f.txt"), "ours\n");
            Run(root, "commit", "-am", "ours");

            using var repo = new GitCliRepository(root);
            try
            {
                await repo.MergeAsync("theirs");
            }
            catch (GitException)
            {
                // expected: conflict
            }

            var snap = await repo.RefreshAsync();
            Assert.True(snap.Operation.IsMerging);
            Assert.Contains(snap.Changes, c => c.IsConflict && c.Path == "f.txt");

            await repo.TakeOursAsync("f.txt");
            await repo.ContinueMergeAsync();

            var done = await repo.RefreshAsync();
            Assert.False(done.Operation.IsMerging);
            Assert.DoesNotContain(done.Changes, c => c.IsConflict);
            Assert.Equal("ours\n", (await File.ReadAllTextAsync(Path.Combine(root, "f.txt"))).Replace("\r\n", "\n"));
        }
        finally
        {
            try
            {
                foreach (var info in new DirectoryInfo(root).GetFileSystemInfos("*", SearchOption.AllDirectories))
                    info.Attributes = FileAttributes.Normal;
                Directory.Delete(root, true);
            }
            catch { }
        }
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
            throw new InvalidOperationException(proc.StandardError.ReadToEnd() + proc.StandardOutput.ReadToEnd());
    }
}
