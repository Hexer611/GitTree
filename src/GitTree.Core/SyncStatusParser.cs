using System.Text.RegularExpressions;

namespace GitTree.Core;

public sealed class SyncStatus
{
    public static readonly SyncStatus None = new();

    public string? Branch { get; init; }
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }

    public bool HasUpstream => !string.IsNullOrWhiteSpace(Upstream);
    public bool HasAhead => Ahead > 0;
    public bool HasBehind => Behind > 0;
    public bool IsInSync => HasUpstream && Ahead == 0 && Behind == 0;
}

public static class SyncStatusParser
{
    private static readonly Regex Header = new(
        @"^##\s+(?<branch>\S+?)(?:\.{3}(?<upstream>\S+))?(?:\s+\[(?<track>[^\]]+)\])?\s*$",
        RegexOptions.Compiled);

    private static readonly Regex Ahead = new(@"ahead\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Behind = new(@"behind\s+(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SyncStatus ParsePorcelain(string porcelain)
    {
        if (string.IsNullOrWhiteSpace(porcelain))
            return SyncStatus.None;

        var first = porcelain.Replace("\r\n", "\n").Split('\n')[0].TrimEnd();
        if (first.StartsWith("## HEAD (no branch)", StringComparison.Ordinal)
            || first.Equals("## HEAD", StringComparison.Ordinal))
            return new SyncStatus { Branch = "HEAD" };

        var unborn = Regex.Match(first, @"^## No commits yet on (\S+)");
        if (unborn.Success)
            return new SyncStatus { Branch = unborn.Groups[1].Value };

        var match = Header.Match(first);
        if (!match.Success)
            return SyncStatus.None;

        var branch = match.Groups["branch"].Value;
        if (branch.Equals("HEAD", StringComparison.Ordinal))
            return new SyncStatus { Branch = branch };

        var track = match.Groups["track"].Value;
        return new SyncStatus
        {
            Branch = branch,
            Upstream = match.Groups["upstream"].Success ? match.Groups["upstream"].Value : null,
            Ahead = Count(Ahead, track),
            Behind = Count(Behind, track)
        };
    }

    public static (int Ahead, int Behind) ParseTrack(string? track)
    {
        if (string.IsNullOrWhiteSpace(track))
            return (0, 0);
        return (Count(Ahead, track), Count(Behind, track));
    }

    private static int Count(Regex regex, string text)
    {
        var match = regex.Match(text);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : 0;
    }
}
