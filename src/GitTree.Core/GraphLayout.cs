namespace GitTree.Core;

public static class GraphLayout
{
    public static void Assign(IList<CommitNode> commits)
    {
        if (commits.Count == 0)
            return;

        var rowOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < commits.Count; i++)
            rowOf.TryAdd(commits[i].Sha, i);

        var branchOf = new Dictionary<string, int>(StringComparer.Ordinal);
        var ranges = new List<(int Start, int End)>();

        void Extend(int branch, int row)
        {
            var range = ranges[branch];
            ranges[branch] = (Math.Min(range.Start, row), Math.Max(range.End, row));
        }

        int NewBranch()
        {
            ranges.Add((int.MaxValue, int.MinValue));
            return ranges.Count - 1;
        }

        // First-parent chains are walked before the branches merged into them.
        // A newer side branch therefore cannot take the column of a commit that
        // the line it was merged into already owns.
        void Walk(string sha, int branch)
        {
            if (!rowOf.TryGetValue(sha, out var row) || !branchOf.TryAdd(sha, branch))
                return;

            Extend(branch, row);
            var parents = Parents(commits[row]);
            if (parents.Length == 0)
                return;

            if (branchOf.ContainsKey(parents[0]))
                Extend(branch, rowOf[parents[0]]);
            else
                Walk(parents[0], branch);

            for (var p = 1; p < parents.Length; p++)
            {
                var parent = parents[p];
                if (branchOf.TryGetValue(parent, out var joined))
                {
                    Extend(joined, row);
                    continue;
                }

                if (!rowOf.ContainsKey(parent))
                    continue;

                var side = NewBranch();
                Walk(parent, side);
                Extend(side, row);
            }
        }

        for (var i = 0; i < commits.Count; i++)
        {
            if (!branchOf.ContainsKey(commits[i].Sha))
                Walk(commits[i].Sha, NewBranch());
        }

        var colorOfBranch = ColorRanges(ranges);
        foreach (var commit in commits)
            commit.Lane = colorOfBranch[branchOf[commit.Sha]];

        var active = new Dictionary<int, string>();
        foreach (var commit in commits)
        {
            var incoming = active
                .Where(pair => pair.Value == commit.Sha && pair.Key != commit.Lane)
                .Select(pair => pair.Key)
                .OrderBy(lane => lane)
                .ToArray();

            var passing = active.Keys
                .Where(lane => !incoming.Contains(lane))
                .ToList();
            if (!passing.Contains(commit.Lane)
                && active.TryGetValue(commit.Lane, out var waiting)
                && waiting == commit.Sha)
            {
                passing.Add(commit.Lane);
            }

            commit.IncomingLanes = incoming;
            commit.PassingLanes = passing.OrderBy(lane => lane).ToArray();

            foreach (var lane in active.Where(pair => pair.Value == commit.Sha).Select(pair => pair.Key).ToArray())
                active.Remove(lane);

            var parents = Parents(commit);
            var edges = new List<GraphEdge>();
            for (var p = 0; p < parents.Length; p++)
            {
                var parent = parents[p];
                int toLane;
                if (p == 0)
                {
                    toLane = commit.Lane;
                }
                else
                {
                    var already = active.FirstOrDefault(pair => pair.Value == parent);
                    if (already.Value == parent)
                    {
                        toLane = already.Key;
                    }
                    else if (branchOf.TryGetValue(parent, out var parentBranch))
                    {
                        toLane = colorOfBranch[parentBranch];
                        if (active.TryGetValue(toLane, out var occupant) && occupant != parent)
                            toLane = FirstFree(active);
                    }
                    else
                    {
                        toLane = FirstFree(active);
                    }
                }

                active[toLane] = parent;
                edges.Add(new GraphEdge(commit.Lane, toLane, Math.Max(toLane, commit.Lane), parent));
            }

            commit.Edges = edges;
        }

        var tracks = 1;
        foreach (var commit in commits)
        {
            var max = commit.Lane;
            if (commit.PassingLanes.Count > 0)
                max = Math.Max(max, commit.PassingLanes.Max());
            if (commit.IncomingLanes.Count > 0)
                max = Math.Max(max, commit.IncomingLanes.Max());
            if (commit.Edges.Count > 0)
                max = Math.Max(max, commit.Edges.Max(edge => Math.Max(edge.FromLane, edge.ToLane)));
            tracks = Math.Max(tracks, max + 1);
        }

        foreach (var commit in commits)
            commit.TrackCount = tracks;
    }

    private static string[] Parents(CommitNode commit) =>
        commit.ParentShas.Where(parent => !string.IsNullOrWhiteSpace(parent)).ToArray();

    private static int[] ColorRanges(List<(int Start, int End)> ranges)
    {
        var color = new int[ranges.Count];
        var open = new List<(int End, int Color)>();
        foreach (var id in Enumerable.Range(0, ranges.Count).OrderBy(i => ranges[i].Start).ThenBy(i => ranges[i].End))
        {
            if (ranges[id].Start == int.MaxValue)
                continue;

            open.RemoveAll(item => item.End < ranges[id].Start);
            var used = open.Select(item => item.Color).ToHashSet();
            var assigned = 0;
            while (used.Contains(assigned))
                assigned++;

            color[id] = assigned;
            open.Add((ranges[id].End, assigned));
        }

        return color;
    }

    private static int FirstFree(Dictionary<int, string> active)
    {
        var lane = 0;
        while (active.ContainsKey(lane))
            lane++;
        return lane;
    }
}
