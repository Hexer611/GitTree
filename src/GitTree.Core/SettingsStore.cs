using System.Text.Json;

namespace GitTree.Core;

public sealed class AppSettings
{
    public List<string> RecentRepositories { get; set; } = [];
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

    public void RememberRepository(string path)
    {
        var settings = Load();
        settings.RecentRepositories.RemoveAll(p =>
            string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        settings.RecentRepositories.Insert(0, path);
        if (settings.RecentRepositories.Count > 12)
            settings.RecentRepositories.RemoveRange(12, settings.RecentRepositories.Count - 12);
        Save(settings);
    }
}
