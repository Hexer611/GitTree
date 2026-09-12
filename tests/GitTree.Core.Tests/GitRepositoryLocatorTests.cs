using GitTree.Core;

namespace GitTree.Core.Tests;

public class GitRepositoryLocatorTests
{
    [Fact]
    public void FindProjectRoot_MainRepo_IsTheWorkingTree()
    {
        var root = CreateTempRepo();
        try
        {
            var project = GitRepositoryLocator.FindProjectRoot(root);
            Assert.True(GitRepositoryLocator.PathsEqual(root, project!));
            Assert.True(GitRepositoryLocator.IsSameProject(root, Path.Combine(root, "nested")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FindProjectRoot_LinkedWorktree_ResolvesToMainCheckout()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "base\n");
            Run(root, "add", "a.txt");
            Run(root, "commit", "-m", "base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            var fromMain = GitRepositoryLocator.FindProjectRoot(root);
            var fromWorktree = GitRepositoryLocator.FindProjectRoot(wt);
            Assert.True(GitRepositoryLocator.PathsEqual(root, fromMain!));
            Assert.True(GitRepositoryLocator.PathsEqual(root, fromWorktree!));
            Assert.True(GitRepositoryLocator.IsSameProject(root, wt));
            Assert.False(GitRepositoryLocator.PathsEqual(root, wt));
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
        }
    }

    [Fact]
    public void CollapseToProjects_DropsLinkedWorktrees()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        var other = CreateTempRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "base\n");
            Run(root, "add", "a.txt");
            Run(root, "commit", "-m", "base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            var collapsed = GitRepositoryLocator.CollapseToProjects([wt, other, root]);
            Assert.Equal(2, collapsed.Count);
            Assert.True(GitRepositoryLocator.PathsEqual(root, collapsed[0]));
            Assert.True(GitRepositoryLocator.PathsEqual(other, collapsed[1]));
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
            TryDelete(other);
        }
    }

    [Fact]
    public void CollapseToProjects_DropsCursorWorktreesAndKeepsProject()
    {
        var project = @"D:\Users\erdem\Documents\3Designer";
        var other = @"D:\Users\erdem\Documents\GitTree";
        var collapsed = GitRepositoryLocator.CollapseToProjects(
        [
            other,
            project,
            @"C:\Users\PC\.cursor\worktrees\3Designer\7owv",
            @"C:\Users\PC\.cursor\worktrees\3Designer\hsac",
            @"C:\Users\PC\.cursor\worktrees\GitTree\abcd"
        ]);

        Assert.Equal(2, collapsed.Count);
        Assert.True(GitRepositoryLocator.PathsEqual(other, collapsed[0]));
        Assert.True(GitRepositoryLocator.PathsEqual(project, collapsed[1]));
    }

    [Fact]
    public void CollapseToProjects_LeadingWorktreeBumpsMatchingProject()
    {
        var project = @"D:\Users\erdem\Documents\3Designer";
        var other = @"D:\Users\erdem\Documents\GitTree";
        var collapsed = GitRepositoryLocator.CollapseToProjects(
        [
            @"C:\Users\PC\.cursor\worktrees\3Designer\7owv",
            other,
            project
        ]);

        Assert.Equal(2, collapsed.Count);
        Assert.True(GitRepositoryLocator.PathsEqual(project, collapsed[0]));
        Assert.True(GitRepositoryLocator.PathsEqual(other, collapsed[1]));
    }

    [Fact]
    public void CollapseToProjects_DropsOrphanCursorWorktrees()
    {
        var collapsed = GitRepositoryLocator.CollapseToProjects(
        [
            @"C:\Users\PC\.cursor\worktrees\3Designer\7owv",
            @"C:\Users\PC\.cursor\worktrees\3Designer\hsac"
        ]);

        Assert.Empty(collapsed);
    }

    [Fact]
    public void ResolveProjectForRecents_MapsCursorWorktreeByName()
    {
        var project = @"D:\Users\erdem\Documents\3Designer";
        var mapped = GitRepositoryLocator.ResolveProjectForRecents(
            @"C:\Users\PC\.cursor\worktrees\3Designer\7owv",
            [project, @"D:\Users\erdem\Documents\GitTree"]);

        Assert.True(GitRepositoryLocator.PathsEqual(project, mapped!));
        Assert.True(GitRepositoryLocator.IsSameProject(project, @"C:\Users\PC\.cursor\worktrees\3Designer\7owv"));
        Assert.True(GitRepositoryLocator.IsAuxiliaryWorktreePath(@"C:\Users\PC\.cursor\worktrees\3Designer\7owv"));
        Assert.False(GitRepositoryLocator.IsAuxiliaryWorktreePath(project));
    }

    [Fact]
    public void RememberRepository_DoesNotStoreCursorWorktree()
    {
        var settingsFile = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        try
        {
            File.WriteAllText(settingsFile, """
                {
                  "RecentRepositories": [
                    "D:\\Users\\erdem\\Documents\\3Designer",
                    "C:\\Users\\PC\\.cursor\\worktrees\\3Designer\\7owv"
                  ]
                }
                """);

            var store = new SettingsStore(settingsFile);
            store.RememberRepository(@"C:\Users\PC\.cursor\worktrees\3Designer\hsac");

            var recents = store.Load().RecentRepositories;
            Assert.Single(recents);
            Assert.True(GitRepositoryLocator.PathsEqual(@"D:\Users\erdem\Documents\3Designer", recents[0]));
        }
        finally
        {
            try { File.Delete(settingsFile); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LoadRecentProjects_RewritesSettingsWithoutWorktrees()
    {
        var settingsFile = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        try
        {
            File.WriteAllText(settingsFile, """
                {
                  "RecentRepositories": [
                    "D:\\Users\\erdem\\Documents\\GitTree",
                    "D:\\Users\\erdem\\Documents\\3Designer",
                    "C:\\Users\\PC\\.cursor\\worktrees\\3Designer\\7owv"
                  ]
                }
                """);

            var store = new SettingsStore(settingsFile);
            var projects = store.LoadRecentProjects();
            Assert.Equal(2, projects.Count);
            Assert.DoesNotContain(projects, p => GitRepositoryLocator.IsAuxiliaryWorktreePath(p));

            var saved = store.Load().RecentRepositories;
            Assert.Equal(2, saved.Count);
            Assert.DoesNotContain(saved, p => GitRepositoryLocator.IsAuxiliaryWorktreePath(p));
        }
        finally
        {
            try { File.Delete(settingsFile); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void RememberRepository_StoresProjectRootNotWorktree()
    {
        var root = CreateTempRepo();
        var wt = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + "-wt");
        var settingsFile = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(Path.Combine(root, "a.txt"), "base\n");
            Run(root, "add", "a.txt");
            Run(root, "commit", "-m", "base");
            Run(root, "branch", "other");
            Run(root, "worktree", "add", wt, "other");

            var store = new SettingsStore(settingsFile);
            store.RememberRepository(wt);
            store.RememberRepository(root);

            var recents = store.Load().RecentRepositories;
            Assert.Single(recents);
            Assert.True(GitRepositoryLocator.PathsEqual(root, recents[0]));
        }
        finally
        {
            TryDelete(wt);
            TryDelete(root);
            try { File.Delete(settingsFile); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SettingsStore_RoundTripsSidebarSectionLayout()
    {
        var settingsFile = Path.Combine(Path.GetTempPath(), "gittree-tests", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        try
        {
            var store = new SettingsStore(settingsFile);
            store.Save(new AppSettings
            {
                SidebarLocalExpanded = false,
                SidebarRemotesExpanded = false,
                SidebarTagsExpanded = true,
                SidebarStashesExpanded = true,
                SidebarWorktreesExpanded = false
            });

            var loaded = store.Load();
            Assert.False(loaded.SidebarLocalExpanded);
            Assert.False(loaded.SidebarRemotesExpanded);
            Assert.True(loaded.SidebarTagsExpanded);
            Assert.True(loaded.SidebarStashesExpanded);
            Assert.False(loaded.SidebarWorktreesExpanded);
        }
        finally
        {
            try { File.Delete(settingsFile); } catch { /* ignore */ }
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
