namespace GitTree.Core;

public static class GitLogParser
{
    public const char FieldSep = '\u001f';
    public const char RecordSep = '\u001e';

    /// <summary>
    /// Format used with git log: %H%x1f%P%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%D%x1e
    /// </summary>
    public static IReadOnlyList<CommitNode> Parse(string output)
    {
        var commits = new List<CommitNode>();
        if (string.IsNullOrWhiteSpace(output))
            return commits;

        foreach (var record in output.Split(RecordSep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = record.Split(FieldSep);
            if (fields.Length < 6)
                continue;

            var sha = fields[0].Trim();
            if (sha.Length < 4)
                continue;

            var parents = string.IsNullOrWhiteSpace(fields[1])
                ? Array.Empty<string>()
                : fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            DateTimeOffset.TryParse(fields[4], out var date);

            var decorations = Array.Empty<string>();
            if (fields.Length > 6 && !string.IsNullOrWhiteSpace(fields[6]))
            {
                decorations = fields[6]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(CleanDecoration)
                    .Where(s => s.Length > 0)
                    .ToArray();
            }

            commits.Add(new CommitNode
            {
                Sha = sha,
                ParentShas = parents,
                AuthorName = fields[2],
                AuthorEmail = fields[3],
                AuthorDate = date == default ? DateTimeOffset.Now : date,
                Subject = fields[5].Replace('\n', ' ').Replace('\r', ' '),
                Decorations = decorations
            });
        }

        GraphLayout.Assign(commits);
        return commits;
    }

    private static string CleanDecoration(string value)
    {
        const string headPrefix = "HEAD -> ";
        if (value.StartsWith(headPrefix, StringComparison.Ordinal))
            return value[headPrefix.Length..];
        if (value.Equals("HEAD", StringComparison.Ordinal))
            return "HEAD";
        if (value.StartsWith("tag: ", StringComparison.Ordinal))
            return value;
        return value;
    }
}
