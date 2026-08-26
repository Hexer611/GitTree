namespace GitTree.Core;

public static class DiffLineParser
{
    public static IReadOnlyList<DiffLine> Parse(string diff)
    {
        var lines = new List<DiffLine>();
        if (string.IsNullOrEmpty(diff))
            return lines;

        foreach (var raw in diff.Replace("\r\n", "\n").Split('\n'))
        {
            DiffLineKind kind;
            if (raw.StartsWith("+++", StringComparison.Ordinal) || raw.StartsWith("---", StringComparison.Ordinal)
                || raw.StartsWith("diff ", StringComparison.Ordinal) || raw.StartsWith("index ", StringComparison.Ordinal)
                || raw.StartsWith("new file", StringComparison.Ordinal) || raw.StartsWith("deleted file", StringComparison.Ordinal)
                || raw.StartsWith("similarity", StringComparison.Ordinal) || raw.StartsWith("rename", StringComparison.Ordinal))
            {
                kind = DiffLineKind.Meta;
            }
            else if (raw.StartsWith("@@", StringComparison.Ordinal))
                kind = DiffLineKind.Hunk;
            else if (raw.StartsWith('+'))
                kind = DiffLineKind.Added;
            else if (raw.StartsWith('-'))
                kind = DiffLineKind.Removed;
            else
                kind = DiffLineKind.Context;

            lines.Add(new DiffLine(kind, raw));
        }

        return lines;
    }
}

public enum DiffLineKind
{
    Context,
    Added,
    Removed,
    Hunk,
    Meta
}

public sealed record DiffLine(DiffLineKind Kind, string Text);
