namespace GitTree.Core;

public sealed class GraphEdge
{
    public GraphEdge(int fromLane, int toLane, int colorIndex, string parentSha)
    {
        FromLane = fromLane;
        ToLane = toLane;
        ColorIndex = colorIndex;
        ParentSha = parentSha;
    }

    public int FromLane { get; }
    public int ToLane { get; private set; }
    public int ColorIndex { get; private set; }
    public string ParentSha { get; }

    internal void Retarget(int toLane)
    {
        ToLane = toLane;
        ColorIndex = Math.Max(toLane, FromLane);
    }
}

public sealed class CommitNode
{
    public required string Sha { get; init; }
    public required IReadOnlyList<string> ParentShas { get; init; }
    public required string AuthorName { get; init; }
    public required string AuthorEmail { get; init; }
    public required DateTimeOffset AuthorDate { get; init; }
    public required string Subject { get; init; }
    public IReadOnlyList<string> Decorations { get; set; } = [];
    public bool IsHead { get; set; }
    public int Lane { get; set; }
    public int TrackCount { get; set; }
    public IReadOnlyList<int> PassingLanes { get; set; } = [];
    public IReadOnlyList<int> IncomingLanes { get; set; } = [];
    public IReadOnlyList<GraphEdge> Edges { get; set; } = [];
    public bool GraphHighlightActive { get; private set; }
    public bool HighlightDot { get; private set; }
    public IReadOnlyList<int> HighlightLanes { get; private set; } = [];
    public event Action? GraphHighlightChanged;

    public void SetGraphHighlight(bool active, bool dot, IReadOnlyList<int> lanes)
    {
        if (GraphHighlightActive == active
            && HighlightDot == dot
            && HighlightLanes.SequenceEqual(lanes))
            return;

        GraphHighlightActive = active;
        HighlightDot = dot;
        HighlightLanes = lanes;
        GraphHighlightChanged?.Invoke();
    }

    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;
    public bool IsMerge => ParentShas.Count > 1;
}
