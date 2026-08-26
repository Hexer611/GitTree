namespace GitTree.Core;

public static class StatusPorcelainParser
{
    public static FileChangeKind ParseStatusChar(char c) => c switch
    {
        'M' => FileChangeKind.Modified,
        'A' => FileChangeKind.Added,
        'D' => FileChangeKind.Deleted,
        'R' => FileChangeKind.Renamed,
        'C' => FileChangeKind.Copied,
        'U' => FileChangeKind.Unmerged,
        'T' => FileChangeKind.TypeChange,
        '?' => FileChangeKind.Untracked,
        '!' => FileChangeKind.Ignored,
        ' ' => FileChangeKind.Unmodified,
        _ => FileChangeKind.Modified
    };

    public static IReadOnlyList<FileChange> Parse(string porcelain)
    {
        var changes = new List<FileChange>();
        if (string.IsNullOrEmpty(porcelain))
            return changes;

        foreach (var rawLine in porcelain.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length < 2 || line.StartsWith('#'))
                continue;

            var indexChar = line[0];
            var workChar = line.Length > 1 ? line[1] : ' ';
            var rest = line.Length > 3 ? line[3..] : "";

            string path;
            string? oldPath = null;
            var arrow = rest.IndexOf(" -> ", StringComparison.Ordinal);
            if (arrow >= 0 && (indexChar is 'R' or 'C' || workChar is 'R' or 'C'))
            {
                oldPath = Unquote(rest[..arrow].Trim());
                path = Unquote(rest[(arrow + 4)..].Trim());
            }
            else
            {
                path = Unquote(rest.Trim());
            }

            if (string.IsNullOrEmpty(path))
                continue;

            var indexStatus = ParseStatusChar(indexChar);
            var workStatus = ParseStatusChar(workChar);
            var conflict = indexChar == 'U' || workChar == 'U'
                           || (indexChar == 'A' && workChar == 'A')
                           || (indexChar == 'D' && workChar == 'D');

            changes.Add(new FileChange
            {
                Path = path.Replace('\\', '/'),
                OldPath = oldPath?.Replace('\\', '/'),
                IndexStatus = indexStatus,
                WorkTreeStatus = workStatus,
                IsConflict = conflict
            });
        }

        return changes;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\"", "\"");
        return value;
    }
}
