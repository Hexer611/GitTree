namespace GitTree.Core;

public sealed class GraphEdge
{
    public GraphEdge(int fromLane, int toLane, int colorIndex)
    {
        FromLane = fromLane;
        ToLane = toLane;
        ColorIndex = colorIndex;
    }

    public int FromLane { get; }
    public int ToLane { get; }
    public int ColorIndex { get; }
}

public sealed class CommitNode
{
    public required string Sha { get; init; }
    public required IReadOnlyList<string> ParentShas { get; init; }
    public required string AuthorName { get; init; }
    public required string AuthorEmail { get; init; }
    public required DateTimeOffset AuthorDate { get; init; }
    public required string Subject { get; init; }
    public IReadOnlyList<string> Decorations { get; init; } = [];
    public int Lane { get; set; }
    public IReadOnlyList<int> PassingLanes { get; set; } = [];
    public IReadOnlyList<GraphEdge> Edges { get; set; } = [];

    public string ShortSha => Sha.Length >= 7 ? Sha[..7] : Sha;
    public bool IsMerge => ParentShas.Count > 1;
}
