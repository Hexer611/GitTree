using System.Text.RegularExpressions;

namespace GitTree.Core;

public static class DiffLineParser
{
    private static readonly Regex HunkHeader = new(
        @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@(.*)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<DiffLine> Parse(string diff)
    {
        var lines = new List<DiffLine>();
        if (string.IsNullOrEmpty(diff))
            return lines;

        var text = diff.Replace("\r\n", "\n");
        var rawLines = text.Split('\n');
        var last = rawLines.Length;
        if (last > 0 && rawLines[last - 1].Length == 0)
            last--;

        var oldLine = 0;
        var newLine = 0;

        for (var i = 0; i < last; i++)
        {
            var raw = rawLines[i];
            if (raw.Length == 0 && lines.Count == 0)
                continue;

            if (IsMeta(raw))
                continue;

            if (raw.StartsWith("@@", StringComparison.Ordinal))
            {
                var match = HunkHeader.Match(raw);
                if (match.Success)
                {
                    oldLine = int.Parse(match.Groups[1].Value);
                    newLine = int.Parse(match.Groups[3].Value);
                    var oldCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1;
                    var newCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1;
                    var context = match.Groups[5].Value.Trim();
                    var title = string.IsNullOrEmpty(context)
                        ? $"Lines {oldLine}–{oldLine + Math.Max(oldCount - 1, 0)}"
                        : $"Lines {oldLine}–{oldLine + Math.Max(oldCount - 1, 0)}  ·  {context}";
                    lines.Add(new DiffLine(DiffLineKind.Hunk, raw)
                    {
                        DisplayText = title,
                        Prefix = "",
                        HunkTitle = title,
                        HunkOldStart = oldLine,
                        HunkNewStart = newLine
                    });
                }
                else
                {
                    lines.Add(new DiffLine(DiffLineKind.Hunk, raw)
                    {
                        DisplayText = raw,
                        Prefix = ""
                    });
                }

                continue;
            }

            if (raw.StartsWith('+'))
            {
                lines.Add(new DiffLine(DiffLineKind.Added, raw)
                {
                    DisplayText = raw[1..],
                    Prefix = "+",
                    NewNumber = newLine
                });
                newLine++;
                continue;
            }

            if (raw.StartsWith('-'))
            {
                lines.Add(new DiffLine(DiffLineKind.Removed, raw)
                {
                    DisplayText = raw[1..],
                    Prefix = "−",
                    OldNumber = oldLine
                });
                oldLine++;
                continue;
            }

            var body = raw.StartsWith(' ') ? raw[1..] : raw;
            lines.Add(new DiffLine(DiffLineKind.Context, raw)
            {
                DisplayText = body,
                Prefix = " ",
                OldNumber = oldLine,
                NewNumber = newLine
            });
            oldLine++;
            newLine++;
        }

        IntraLineDiff.Apply(lines);
        return lines;
    }

    public static IReadOnlyList<DiffLine> ParseNewFile(string content)
    {
        var lines = new List<DiffLine>();
        var text = content.Replace("\r\n", "\n");
        if (text.EndsWith('\n'))
            text = text[..^1];

        var rawLines = text.Length == 0 ? Array.Empty<string>() : text.Split('\n');
        var n = 1;
        lines.Add(new DiffLine(DiffLineKind.Hunk, $"@@ -0,0 +1,{rawLines.Length} @@")
        {
            DisplayText = "New file",
            Prefix = "",
            HunkTitle = "New file",
            HunkOldStart = 0,
            HunkNewStart = 1
        });

        foreach (var raw in rawLines)
        {
            lines.Add(new DiffLine(DiffLineKind.Added, "+" + raw)
            {
                DisplayText = raw,
                Prefix = "+",
                NewNumber = n++
            });
        }

        return lines;
    }

    private static bool IsMeta(string raw) =>
        raw.StartsWith("diff ", StringComparison.Ordinal)
        || raw.StartsWith("index ", StringComparison.Ordinal)
        || raw.StartsWith("new file", StringComparison.Ordinal)
        || raw.StartsWith("deleted file", StringComparison.Ordinal)
        || raw.StartsWith("old mode", StringComparison.Ordinal)
        || raw.StartsWith("new mode", StringComparison.Ordinal)
        || raw.StartsWith("similarity", StringComparison.Ordinal)
        || raw.StartsWith("rename ", StringComparison.Ordinal)
        || raw.StartsWith("copy ", StringComparison.Ordinal)
        || raw.StartsWith("Binary files", StringComparison.Ordinal)
        || raw.StartsWith("GIT binary", StringComparison.Ordinal)
        || raw.StartsWith("---", StringComparison.Ordinal)
        || raw.StartsWith("+++", StringComparison.Ordinal)
        || raw.StartsWith("\\", StringComparison.Ordinal);
}

public static class IntraLineDiff
{
    public static void Apply(IList<DiffLine> lines)
    {
        var i = 0;
        while (i < lines.Count)
        {
            if (lines[i].Kind != DiffLineKind.Removed)
            {
                i++;
                continue;
            }

            var remStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Removed)
                i++;
            var addStart = i;
            while (i < lines.Count && lines[i].Kind == DiffLineKind.Added)
                i++;

            var paired = Math.Min(addStart - remStart, i - addStart);
            for (var k = 0; k < paired; k++)
            {
                var removed = lines[remStart + k];
                var added = lines[addStart + k];
                var (oldSegs, newSegs) = Compare(removed.DisplayText, added.DisplayText);
                lines[remStart + k] = removed with { Segments = oldSegs };
                lines[addStart + k] = added with { Segments = newSegs };
            }
        }
    }

    public static (IReadOnlyList<DiffSegment> Old, IReadOnlyList<DiffSegment> New) Compare(string oldText, string newText)
    {
        if (oldText.Length > 800 || newText.Length > 800)
            return ([], []);

        var a = Tokenize(oldText);
        var b = Tokenize(newText);
        if (a.Count == 0 || b.Count == 0 || a.Count > 240 || b.Count > 240)
            return ([], []);

        var commonA = new bool[a.Count];
        var commonB = new bool[b.Count];
        MarkLcs(a, b, commonA, commonB);

        var oldSegs = ToSegments(a, commonA);
        var newSegs = ToSegments(b, commonB);
        if (oldSegs.All(s => s.Highlight) && newSegs.All(s => s.Highlight))
            return ([], []);

        return (oldSegs, newSegs);
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var start = i;
            if (char.IsWhiteSpace(text[i]))
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
            }
            else if (char.IsLetterOrDigit(text[i]) || text[i] == '_')
            {
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;
            }
            else
            {
                i++;
            }

            tokens.Add(text[start..i]);
        }

        return tokens;
    }

    private static void MarkLcs(IReadOnlyList<string> a, IReadOnlyList<string> b, bool[] commonA, bool[] commonB)
    {
        var n = a.Count;
        var m = b.Count;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
            {
                dp[i, j] = a[i] == b[j]
                    ? dp[i + 1, j + 1] + 1
                    : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        var x = 0;
        var y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y])
            {
                commonA[x] = true;
                commonB[y] = true;
                x++;
                y++;
            }
            else if (dp[x + 1, y] >= dp[x, y + 1])
                x++;
            else
                y++;
        }
    }

    private static IReadOnlyList<DiffSegment> ToSegments(IReadOnlyList<string> tokens, bool[] common)
    {
        var list = new List<DiffSegment>();
        foreach (var (token, i) in tokens.Select((t, i) => (t, i)))
        {
            var highlight = !common[i];
            if (list.Count > 0 && list[^1].Highlight == highlight)
                list[^1] = new DiffSegment(list[^1].Text + token, highlight);
            else
                list.Add(new DiffSegment(token, highlight));
        }

        return list;
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

public sealed record DiffSegment(string Text, bool Highlight);

public sealed record DiffLine(DiffLineKind Kind, string Text)
{
    public string DisplayText { get; init; } = Text;
    public string Prefix { get; init; } = "";
    public int? OldNumber { get; init; }
    public int? NewNumber { get; init; }
    public string? HunkTitle { get; init; }
    public int? HunkOldStart { get; init; }
    public int? HunkNewStart { get; init; }
    public IReadOnlyList<DiffSegment> Segments { get; init; } = [];

    public bool IsChange => Kind is DiffLineKind.Added or DiffLineKind.Removed;

    public string OldNumberText => OldNumber is int n ? n.ToString() : "";
    public string NewNumberText => NewNumber is int n ? n.ToString() : "";
}
