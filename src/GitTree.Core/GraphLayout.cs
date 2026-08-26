namespace GitTree.Core;

public static class GraphLayout
{
    public static void Assign(IList<CommitNode> commits)
    {
        var lanes = new List<string?>();

        foreach (var commit in commits)
        {
            var lane = lanes.IndexOf(commit.Sha);
            if (lane < 0)
            {
                lane = FirstFree(lanes);
                EnsureSize(lanes, lane + 1);
                lanes[lane] = commit.Sha;
            }

            commit.Lane = lane;
            commit.PassingLanes = lanes
                .Select((sha, index) => sha is null ? -1 : index)
                .Where(index => index >= 0)
                .ToArray();

            var edges = new List<GraphEdge>();
            lanes[lane] = null;

            var parents = commit.ParentShas.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            for (var p = 0; p < parents.Length; p++)
            {
                var parent = parents[p];
                var existing = lanes.IndexOf(parent);
                int toLane;
                if (existing >= 0)
                {
                    toLane = existing;
                }
                else if (p == 0 && lanes[lane] is null)
                {
                    lanes[lane] = parent;
                    toLane = lane;
                }
                else
                {
                    toLane = FirstFree(lanes);
                    EnsureSize(lanes, toLane + 1);
                    lanes[toLane] = parent;
                }

                edges.Add(new GraphEdge(lane, toLane, Math.Max(toLane, lane)));
            }

            commit.Edges = edges;
        }

        var tracks = 1;
        foreach (var commit in commits)
        {
            var max = commit.Lane;
            if (commit.PassingLanes.Count > 0)
                max = Math.Max(max, commit.PassingLanes.Max());
            if (commit.Edges.Count > 0)
                max = Math.Max(max, commit.Edges.Max(e => Math.Max(e.FromLane, e.ToLane)));
            tracks = Math.Max(tracks, max + 1);
        }

        foreach (var commit in commits)
            commit.TrackCount = tracks;
    }

    private static int FirstFree(List<string?> lanes)
    {
        var index = lanes.IndexOf(null);
        return index >= 0 ? index : lanes.Count;
    }

    private static void EnsureSize(List<string?> lanes, int size)
    {
        while (lanes.Count < size)
            lanes.Add(null);
    }
}
