namespace GitTree.Core;

public sealed class GitRepositoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _workTree;
    private readonly FileSystemWatcher _gitDir;
    private readonly System.Timers.Timer _debounce;
    private bool _disposed;

    public event EventHandler? Changed;

    public GitRepositoryWatcher(string workingDirectory, int debounceMs = 400)
    {
        var gitDir = Path.Combine(workingDirectory, ".git");

        _workTree = CreateWatcher(workingDirectory);
        _gitDir = Directory.Exists(gitDir)
            ? CreateWatcher(gitDir)
            : CreateWatcher(workingDirectory);

        _debounce = new System.Timers.Timer(debounceMs) { AutoReset = false };
        _debounce.Elapsed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    private FileSystemWatcher CreateWatcher(string path)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += OnFs;
        watcher.Created += OnFs;
        watcher.Deleted += OnFs;
        watcher.Renamed += OnFs;
        return watcher;
    }

    private void OnFs(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}objects", StringComparison.OrdinalIgnoreCase)
            || e.FullPath.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}hooks", StringComparison.OrdinalIgnoreCase))
            return;

        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _debounce.Dispose();
        _workTree.Dispose();
        _gitDir.Dispose();
    }
}
