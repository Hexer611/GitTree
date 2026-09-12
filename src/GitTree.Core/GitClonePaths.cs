namespace GitTree.Core;

public static class GitClonePaths
{
    public static string FolderNameFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";

        var value = url.Trim();
        var hash = value.IndexOf('#');
        if (hash >= 0)
            value = value[..hash];
        var query = value.IndexOf('?');
        if (query >= 0)
            value = value[..query];

        value = value.TrimEnd('/', '\\');
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            value = value[..^4];

        var slash = value.Replace('\\', '/').LastIndexOf('/');
        var colon = value.LastIndexOf(':');
        var cut = slash;
        if (colon > cut && !LooksLikeWindowsDrive(value, colon))
            cut = colon;

        var name = cut >= 0 ? value[(cut + 1)..] : value;
        name = SanitizeFolderName(name);
        return name;
    }

    public static string SuggestDestination(string parentDirectory, string? url)
    {
        var parent = string.IsNullOrWhiteSpace(parentDirectory)
            ? DefaultParentDirectory()
            : parentDirectory.Trim();
        var name = FolderNameFromUrl(url);
        if (string.IsNullOrWhiteSpace(name))
            return parent;
        return Path.Combine(parent, name);
    }

    public static string ResolveParent(string destination, string? url)
    {
        if (string.IsNullOrWhiteSpace(FolderNameFromUrl(url)))
        {
            return string.IsNullOrWhiteSpace(destination)
                ? DefaultParentDirectory()
                : destination.Trim();
        }

        return ParentOf(destination);
    }

    public static string NextDestination(string parentDirectory, string? url, string currentDestination, string lastSuggestion)
    {
        if (!DestinationTracksSuggestion(currentDestination, lastSuggestion))
            return currentDestination;
        return SuggestDestination(parentDirectory, url);
    }

    public static string DefaultParentDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
            return documents;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? Directory.GetCurrentDirectory() : profile;
    }

    public static string ParentOf(string destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
            return DefaultParentDirectory();
        try
        {
            var full = Path.GetFullPath(destination.Trim());
            var parent = Path.GetDirectoryName(full);
            return string.IsNullOrWhiteSpace(parent) ? DefaultParentDirectory() : parent;
        }
        catch
        {
            return DefaultParentDirectory();
        }
    }

    public static bool DestinationTracksSuggestion(string destination, string previousSuggestion)
    {
        if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(previousSuggestion))
            return true;

        try
        {
            return string.Equals(
                Path.GetFullPath(destination.Trim()),
                Path.GetFullPath(previousSuggestion.Trim()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(destination.Trim(), previousSuggestion.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool LooksLikeWindowsDrive(string value, int colon)
        => colon == 1 && char.IsLetter(value[0]);

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(chars).Trim();
    }
}
