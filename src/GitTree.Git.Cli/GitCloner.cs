using GitTree.Core;

namespace GitTree.Git.Cli;

public static class GitCloner
{
    public static async Task CloneAsync(string url, string destination, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("A remote URL is required.", nameof(url));
        if (string.IsNullOrWhiteSpace(destination))
            throw new ArgumentException("A destination folder is required.", nameof(destination));

        var dest = Path.GetFullPath(destination.Trim());
        if (Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any())
            throw new GitException("clone", 128, $"Destination '{dest}' already exists and is not empty.");

        var parent = Path.GetDirectoryName(dest);
        if (string.IsNullOrWhiteSpace(parent))
            throw new ArgumentException("Destination must be a folder path.", nameof(destination));

        Directory.CreateDirectory(parent);
        var runner = new GitCliRunner(parent);
        await runner.RunCaptureAsync(["clone", "--", url.Trim(), dest], cancellationToken);
    }
}
