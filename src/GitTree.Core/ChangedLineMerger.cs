namespace GitTree.Core;

public readonly record struct ChangedLineMergeResult(string Text, bool HasConflict);

/// <summary>
/// Replays only the line edits from <c>baseline → incoming</c> onto <c>current</c>.
/// Current-only lines stay. Where the same baseline line changed on both sides,
/// the incoming line wins — no conflict markers and no whole-file replace.
/// </summary>
public static class ChangedLineMerger
{
    public static ChangedLineMergeResult Apply(string current, string baseline, string incoming)
    {
        current = Normalize(current);
        baseline = Normalize(baseline);
        incoming = Normalize(incoming);
        if (current == incoming)
            return new ChangedLineMergeResult(current, false);
        if (current == baseline)
            return new ChangedLineMergeResult(incoming, false);

        var currentLines = Split(current);
        var baseLines = Split(baseline);
        var incomingLines = Split(incoming);
        var map = MapBasePositionsToCurrent(baseLines, currentLines);
        var hunks = BuildHunks(baseLines, incomingLines);

        foreach (var hunk in hunks.AsEnumerable().Reverse())
        {
            var from = map[hunk.OldStart];
            var to = map[hunk.OldStart + hunk.Removed.Count];
            if (from < 0 || to < from || to > currentLines.Count)
                continue;
            currentLines.RemoveRange(from, to - from);
            currentLines.InsertRange(from, hunk.Added);
        }

        return new ChangedLineMergeResult(Join(currentLines), false);
    }

    private static int[] MapBasePositionsToCurrent(IReadOnlyList<string> baseline, IReadOnlyList<string> current)
    {
        var n = baseline.Count;
        var m = current.Count;
        var map = new int[n + 1];
        var commonA = new bool[n];
        var commonC = new bool[m];
        MarkLcs(baseline, current, commonA, commonC);

        var a = 0;
        var c = 0;
        while (a < n || c < m)
        {
            if (a < n && c < m && commonA[a] && commonC[c])
            {
                map[a] = c;
                a++;
                c++;
                continue;
            }

            var a0 = a;
            var c0 = c;
            while (a < n && !commonA[a])
                a++;
            while (c < m && !commonC[c])
                c++;
            for (var i = a0; i < a; i++)
                map[i] = c0;
        }

        map[n] = c;
        return map;
    }

    private static List<LineHunk> BuildHunks(IReadOnlyList<string> baseline, IReadOnlyList<string> incoming)
    {
        var n = baseline.Count;
        var m = incoming.Count;
        var commonA = new bool[n];
        var commonB = new bool[m];
        MarkLcs(baseline, incoming, commonA, commonB);

        var hunks = new List<LineHunk>();
        var i = 0;
        var j = 0;
        while (i < n || j < m)
        {
            if (i < n && j < m && commonA[i] && commonB[j])
            {
                i++;
                j++;
                continue;
            }

            var oldStart = i;
            var removed = new List<string>();
            var added = new List<string>();
            while (i < n && !commonA[i])
                removed.Add(baseline[i++]);
            while (j < m && !commonB[j])
                added.Add(incoming[j++]);
            if (removed.Count > 0 || added.Count > 0)
                hunks.Add(new LineHunk(oldStart, removed, added));
        }

        return hunks;
    }

    private static void MarkLcs(IReadOnlyList<string> a, IReadOnlyList<string> b, bool[] commonA, bool[] commonB)
    {
        var n = a.Count;
        var m = b.Count;
        if (n == 0 || m == 0)
            return;

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

    private static string Normalize(string text) => text.Replace("\r\n", "\n");

    private static List<string> Split(string text)
    {
        if (text.Length == 0)
            return [];
        var trimmed = text.EndsWith('\n') ? text[..^1] : text;
        return trimmed.Length == 0 ? [""] : [.. trimmed.Split('\n')];
    }

    private static string Join(IReadOnlyList<string> lines)
        => lines.Count == 0 ? "" : string.Join('\n', lines) + "\n";

    private sealed record LineHunk(int OldStart, List<string> Removed, List<string> Added);
}
