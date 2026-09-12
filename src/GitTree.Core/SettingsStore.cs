using System.Text.Json;

namespace GitTree.Core;

public sealed class AppSettings
{
    public List<string> RecentRepositories { get; set; } = [];
    public string ThemeId { get; set; } = "nord";
    public bool RememberWindowPosition { get; set; } = true;
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public string WindowState { get; set; } = "Normal";
    public double SidebarWidth { get; set; }
    public double GraphVsDiffShare { get; set; }
    public double GraphVsFilesShare { get; set; }
    public bool SidebarLocalExpanded { get; set; } = true;
    public bool SidebarRemotesExpanded { get; set; } = true;
    public bool SidebarTagsExpanded { get; set; }
    public bool SidebarStashesExpanded { get; set; }
    public bool SidebarWorktreesExpanded { get; set; } = true;
}

public sealed class SettingsStore
{
    private readonly string _filePath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GitTree");
        Directory.CreateDirectory(dir);
        _filePath = filePath ?? Path.Combine(dir, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public void Update(Action<AppSettings> mutate)
    {
        var settings = Load();
        mutate(settings);
        Save(settings);
    }

    public void RememberRepository(string path)
    {
        Update(settings =>
        {
            settings.RecentRepositories = GitRepositoryLocator.CollapseToProjects(
                new[] { path }.Concat(settings.RecentRepositories));
        });
    }

    public List<string> LoadRecentProjects()
    {
        var settings = Load();
        var projects = GitRepositoryLocator.CollapseToProjects(settings.RecentRepositories);
        if (!PathListsEqual(settings.RecentRepositories, projects))
        {
            try
            {
                settings.RecentRepositories = projects;
                Save(settings);
            }
            catch
            {
                // Keep the in-memory list even if the file cannot be rewritten.
            }
        }

        return projects;
    }

    private static bool PathListsEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
            return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (!GitRepositoryLocator.PathsEqual(left[i], right[i]))
                return false;
        }

        return true;
    }
}
