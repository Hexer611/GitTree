using GitTree.Core;

namespace GitTree.Core.Tests;

public class GitBashTests
{
    [Fact]
    public void CandidateExecutables_IncludesInstallRootsAndGitOnPath()
    {
        var candidates = GitBash.CandidateExecutables(
            programFiles: @"C:\Program Files",
            programFilesX86: @"C:\Program Files (x86)",
            localAppData: @"C:\Users\me\AppData\Local",
            pathEntries: [@"D:\Tools\Git\cmd", @"D:\Tools\Git\mingw64\bin"],
            installPaths: [@"E:\PortableGit"]);

        Assert.Contains(@"C:\Program Files\Git\git-bash.exe", candidates, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Program Files (x86)\Git\git-bash.exe", candidates, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"C:\Users\me\AppData\Local\Programs\Git\git-bash.exe", candidates, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"D:\Tools\Git\git-bash.exe", candidates, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(@"E:\PortableGit\git-bash.exe", candidates, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count(), candidates.Count);
    }

    [Fact]
    public void FindExecutable_ReturnsFirstPathThatExists()
    {
        var candidates = new[]
        {
            @"C:\Missing\git-bash.exe",
            @"D:\Git\git-bash.exe"
        };

        var found = GitBash.FindExecutable(
            candidates,
            path => path.StartsWith(@"D:\", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(@"D:\Git\git-bash.exe", found);
    }

    [Fact]
    public void ExecutableBesideGit_MapsCmdBinAndMingwLayouts()
    {
        Assert.Equal(
            @"C:\Program Files\Git\git-bash.exe",
            GitBash.ExecutableBesideGit(@"C:\Program Files\Git\cmd\git.exe"));
        Assert.Equal(
            @"C:\Program Files\Git\git-bash.exe",
            GitBash.ExecutableBesideGit(@"C:\Program Files\Git\bin\git.exe"));
        Assert.Equal(
            @"C:\Program Files\Git\git-bash.exe",
            GitBash.ExecutableBesideGit(@"C:\Program Files\Git\mingw64\bin\git.exe"));
        Assert.Null(GitBash.ExecutableBesideGit(@"C:\Windows\System32\git.exe"));
    }

    [Fact]
    public void CdArgument_PassesTheFolderAsOneGitBashOption()
    {
        var folder = Path.Combine(Path.GetTempPath(), "git tree repo");
        var argument = GitBash.CdArgument(folder);

        Assert.Equal("--cd=" + Path.GetFullPath(folder), argument);
        Assert.DoesNotContain("\"", argument);
    }

    [Fact]
    public void CdArgument_RejectsAnEmptyFolder()
    {
        Assert.Throws<ArgumentException>(() => GitBash.CdArgument("  "));
    }
}
