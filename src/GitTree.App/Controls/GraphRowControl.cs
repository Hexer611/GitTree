using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitTree.Core;

namespace GitTree.App.Controls;

public sealed class GraphRowControl : Control
{
    public static readonly StyledProperty<CommitNode?> CommitProperty =
        AvaloniaProperty.Register<GraphRowControl, CommitNode?>(nameof(Commit));

    private static readonly Color[] LaneColors =
    [
        Color.Parse("#3DDC97"),
        Color.Parse("#5B8DEF"),
        Color.Parse("#C084FC"),
        Color.Parse("#F5C542"),
        Color.Parse("#FF8A65"),
        Color.Parse("#2EE6D6"),
        Color.Parse("#FF5C7A"),
        Color.Parse("#8B9CFF")
    ];

    static GraphRowControl()
    {
        AffectsRender<GraphRowControl>(CommitProperty);
        AffectsRender<GraphRowControl>(BoundsProperty);
    }

    public CommitNode? Commit
    {
        get => GetValue(CommitProperty);
        set => SetValue(CommitProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var commit = Commit;
        if (commit is null)
            return;

        const double spacing = 16;
        var radius = commit.IsMerge ? 5.4 : 4.4;
        var midY = Bounds.Height / 2;
        var bottom = Bounds.Height;

        IBrush Brush(int lane, byte alpha = 255)
        {
            var c = LaneColors[Math.Abs(lane) % LaneColors.Length];
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        foreach (var lane in commit.PassingLanes)
        {
            var x = 12 + lane * spacing;
            context.DrawLine(new Pen(Brush(lane, 70), 4) { LineCap = PenLineCap.Round }, new Point(x, 0), new Point(x, bottom));
            context.DrawLine(new Pen(Brush(lane), 1.8) { LineCap = PenLineCap.Round }, new Point(x, 0), new Point(x, bottom));
        }

        foreach (var edge in commit.Edges)
        {
            var x0 = 12 + edge.FromLane * spacing;
            var x1 = 12 + edge.ToLane * spacing;
            var penSoft = new Pen(Brush(edge.ColorIndex, 80), 3.4) { LineCap = PenLineCap.Round };
            var pen = new Pen(Brush(edge.ColorIndex), 1.8) { LineCap = PenLineCap.Round };
            context.DrawLine(penSoft, new Point(x0, midY), new Point(x1, bottom));
            context.DrawLine(pen, new Point(x0, midY), new Point(x1, bottom));
        }

        var cx = 12 + commit.Lane * spacing;
        var fill = Brush(commit.Lane);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(55, 61, 220, 151)), null, new Point(cx, midY), radius + 4, radius + 4);
        context.DrawEllipse(fill, new Pen(new SolidColorBrush(Color.FromArgb(230, 12, 16, 22)), 1.4), new Point(cx, midY), radius, radius);
    }
}
