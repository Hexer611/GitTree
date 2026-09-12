namespace GitTree.Core;

public static class StashListParser
{
    /// <summary>
    /// Format used with git stash list pretty-print: %gd%x1f%H%x1f%s
    /// </summary>
    public static IReadOnlyList<StashEntry> Parse(string output)
    {
        var list = new List<StashEntry>();
        var index = 0;
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = SplitFields(line);
            var selector = parts.Length > 0 && parts[0].Length > 0 ? parts[0] : $"stash@{{{index}}}";
            var sha = parts.Length > 1 ? parts[1] : "";
            var message = parts.Length > 2 ? parts[2] : line;
            list.Add(new StashEntry { Index = index, Selector = selector, Sha = sha, Message = message });
            index++;
        }

        return list;
    }

    public static string FormatLabel(string message, string selector)
    {
        if (TrySplitSubject(message, out var branch, out var name))
            return $"{branch} • {name}";
        return string.IsNullOrWhiteSpace(message) ? selector : message;
    }

    internal static bool TrySplitSubject(string message, out string branch, out string name)
    {
        branch = "";
        name = "";
        if (string.IsNullOrWhiteSpace(message))
            return false;

        const string wipPrefix = "WIP on ";
        const string onPrefix = "On ";
        string rest;
        if (message.StartsWith(wipPrefix, StringComparison.Ordinal))
            rest = message[wipPrefix.Length..];
        else if (message.StartsWith(onPrefix, StringComparison.Ordinal))
            rest = message[onPrefix.Length..];
        else
            return false;

        var sep = rest.IndexOf(": ", StringComparison.Ordinal);
        if (sep <= 0)
            return false;

        branch = rest[..sep];
        name = rest[(sep + 2)..];
        return branch.Length > 0 && name.Length > 0;
    }

    private static string[] SplitFields(string line)
    {
        if (line.Contains('\u001f'))
            return line.Split('\u001f');
        if (line.Contains("%1f", StringComparison.Ordinal))
            return line.Split("%1f", StringSplitOptions.None);
        return [line];
    }
}
