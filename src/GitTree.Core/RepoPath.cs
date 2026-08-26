using GitTree.Core;

namespace GitTree.Core;

public static class GitRepositoryLocator
{
    public static string? FindRoot(string path)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(path));
        while (dir is not null)
        {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
