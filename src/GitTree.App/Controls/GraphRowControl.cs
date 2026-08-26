using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitTree.Core;

namespace GitTree.App.Controls;

public sealed class GraphRowControl : Control
{
    public static readonly StyledProperty<CommitNode?> CommitProperty =
        AvaloniaProperty.Register<GraphRowControl, CommitNode?>(nameof(Commit));

    private static readonly IBrush[] LaneBrushes =
    [
        Brush("#4FC3F7"),
        Brush("#81C784"),
        Brush("#FFD54F"),
        Brush("#FF8A65"),
        Brush("#CE93D8"),
        Brush("#4DB6AC"),
        Brush("#F06292"),
        Brush("#90CAF9")
    ];

    static GraphRowControl()
    {
        AffectsRender<GraphRowControl>(CommitProperty);
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

        const double spacing = 14;
        const double radius = 4.5;
        var midY = Bounds.Height / 2;
        var bottom = Bounds.Height;

        foreach (var lane in commit.PassingLanes)
        {
            var x = 10 + lane * spacing;
            var brush = LaneBrushes[lane % LaneBrushes.Length];
            context.DrawLine(new Pen(brush, 1.6), new Point(x, 0), new Point(x, bottom));
        }

        foreach (var edge in commit.Edges)
        {
            var x0 = 10 + edge.FromLane * spacing;
            var x1 = 10 + edge.ToLane * spacing;
            var brush = LaneBrushes[Math.Abs(edge.ColorIndex) % LaneBrushes.Length];
            var pen = new Pen(brush, 1.6);
            if (edge.FromLane == edge.ToLane)
                context.DrawLine(pen, new Point(x0, midY), new Point(x1, bottom));
            else
                context.DrawLine(pen, new Point(x0, midY), new Point(x1, bottom));
        }

        var cx = 10 + commit.Lane * spacing;
        var fill = LaneBrushes[commit.Lane % LaneBrushes.Length];
        context.DrawEllipse(fill, new Pen(Brushes.White, 1.2), new Point(cx, midY), radius, radius);
    }

    private static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));
}
