using Microsoft.Win32;

namespace GitTree.Core;

public static class GitBash
{
    public static string? FindInstalled()
    {
        return FindExecutable(
            CandidateExecutables(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                SplitPath(Environment.GetEnvironmentVariable("PATH")),
                InstallPathsFromRegistry()),
            File.Exists);
    }

    public static string? FindExecutable(IEnumerable<string> candidates, Func<string, bool> exists)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && exists(candidate))
                return candidate;
        }

        return null;
    }

    public static IReadOnlyList<string> CandidateExecutables(
        string? programFiles,
        string? programFilesX86,
        string? localAppData,
        IEnumerable<string>? pathEntries,
        IEnumerable<string>? installPaths)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Add(candidates, seen, UnderGitRoot(programFiles));
        Add(candidates, seen, UnderGitRoot(programFilesX86));
        Add(candidates, seen, string.IsNullOrWhiteSpace(localAppData)
            ? null
            : Path.Combine(localAppData, "Programs", "Git", "git-bash.exe"));

        if (installPaths is not null)
        {
            foreach (var installPath in installPaths)
                Add(candidates, seen, BashInInstallRoot(installPath));
        }

        if (pathEntries is not null)
        {
            foreach (var entry in pathEntries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;
                Add(candidates, seen, ExecutableBesideGit(Path.Combine(entry.Trim().Trim('"'), "git.exe")));
            }
        }

        return candidates;
    }

    public static string? ExecutableBesideGit(string gitExecutable)
    {
        var directory = Path.GetDirectoryName(gitExecutable);
        if (string.IsNullOrEmpty(directory))
            return null;

        var folder = Path.GetFileName(directory);
        if (folder.Equals("cmd", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Path.GetDirectoryName(directory) ?? "", "git-bash.exe");

        if (!folder.Equals("bin", StringComparison.OrdinalIgnoreCase))
            return null;

        var parent = Path.GetDirectoryName(directory);
        if (string.IsNullOrEmpty(parent))
            return null;

        var parentName = Path.GetFileName(parent);
        if (parentName.Equals("mingw64", StringComparison.OrdinalIgnoreCase)
            || parentName.Equals("mingw32", StringComparison.OrdinalIgnoreCase)
            || parentName.Equals("usr", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(parent);
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, "git-bash.exe");
        }

        return Path.Combine(parent, "git-bash.exe");
    }

    public static string CdArgument(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException("A repository folder is required.", nameof(directory));
        return "--cd=" + Path.GetFullPath(directory.Trim());
    }

    private static string? UnderGitRoot(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return null;
        return Path.Combine(root.Trim(), "Git", "git-bash.exe");
    }

    private static string? BashInInstallRoot(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
            return null;
        return Path.Combine(installPath.Trim(), "git-bash.exe");
    }

    private static void Add(List<string> candidates, HashSet<string> seen, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (seen.Add(full))
            candidates.Add(full);
    }

    private static IEnumerable<string> SplitPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return entry;
    }

    private static IEnumerable<string> InstallPathsFromRegistry()
    {
        if (!OperatingSystem.IsWindows())
            yield break;

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                string? installPath = null;
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\GitForWindows");
                    installPath = key?.GetValue("InstallPath") as string;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    installPath = null;
                }

                if (!string.IsNullOrWhiteSpace(installPath))
                    yield return installPath;
            }
        }
    }
}
