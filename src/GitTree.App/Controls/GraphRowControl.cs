using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using GitTree.Core;

namespace GitTree.App.Controls;

public sealed class GraphRowControl : Control
{
    public static readonly StyledProperty<CommitNode?> CommitProperty =
        AvaloniaProperty.Register<GraphRowControl, CommitNode?>(nameof(Commit));

    private const double Spacing = 18;
    private const double PadX = 14;

    private static readonly Color[] LaneColors =
    [
        Color.Parse("#3DDC97"),
        Color.Parse("#5B8DEF"),
        Color.Parse("#C084FC"),
        Color.Parse("#F5C542"),
        Color.Parse("#2EE6D6"),
        Color.Parse("#FF8A65"),
        Color.Parse("#FF5C7A"),
        Color.Parse("#8B9CFF")
    ];

    static GraphRowControl()
    {
        AffectsRender<GraphRowControl>(CommitProperty);
        AffectsRender<GraphRowControl>(BoundsProperty);
        AffectsMeasure<GraphRowControl>(CommitProperty);
    }

    public CommitNode? Commit
    {
        get => GetValue(CommitProperty);
        set => SetValue(CommitProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var tracks = Math.Max(3, Commit?.TrackCount ?? 3);
        return new Size(PadX + tracks * Spacing + 8, 40);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var commit = Commit;
        if (commit is null)
            return;

        var midY = Bounds.Height / 2;
        var bottom = Bounds.Height;
        var isHead = commit.Decorations.Any(d =>
            d.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || d.StartsWith("HEAD ", StringComparison.OrdinalIgnoreCase)
            || d.StartsWith("HEAD ->", StringComparison.OrdinalIgnoreCase));
        var radius = commit.IsMerge ? 6.2 : isHead ? 5.6 : 4.8;
        var cx = X(commit.Lane);

        Color LaneColor(int lane) => LaneColors[Math.Abs(lane) % LaneColors.Length];

        IBrush Brush(int lane, byte alpha = 255)
        {
            var c = LaneColor(lane);
            return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        Pen LinePen(int lane, double width, byte alpha = 255) =>
            new(Brush(lane, alpha), width)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

        foreach (var lane in commit.PassingLanes)
        {
            var x = X(lane);
            if (lane == commit.Lane)
            {
                DrawLine(context, LinePen(lane, 5.2, 42), new Point(x, 0), new Point(x, midY - radius - 1.5));
                DrawLine(context, LinePen(lane, 2.15), new Point(x, 0), new Point(x, midY - radius - 1.5));
                continue;
            }

            DrawLine(context, LinePen(lane, 5.2, 42), new Point(x, 0), new Point(x, bottom));
            DrawLine(context, LinePen(lane, 2.15), new Point(x, 0), new Point(x, bottom));
        }

        foreach (var edge in commit.Edges)
        {
            var from = new Point(X(edge.FromLane), midY + radius * 0.15);
            var to = new Point(X(edge.ToLane), bottom);
            var colorLane = edge.ToLane;
            DrawCurve(context, LinePen(colorLane, 5.4, 40), from, to);
            DrawCurve(context, LinePen(colorLane, 2.2), from, to);
        }

        var color = LaneColor(commit.Lane);
        var glow = new SolidColorBrush(Color.FromArgb(isHead ? (byte)90 : (byte)50, color.R, color.G, color.B));
        context.DrawEllipse(glow, null, new Point(cx, midY), radius + (isHead ? 7 : 5.2), radius + (isHead ? 7 : 5.2));

        var fill = new RadialGradientBrush
        {
            Center = new RelativePoint(0.32, 0.28, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Lighten(color, 0.28), 0),
                new GradientStop(color, 0.7),
                new GradientStop(Darken(color, 0.18), 1)
            }
        };

        var ring = new Pen(new SolidColorBrush(Color.FromArgb(230, 10, 14, 20)), 1.6);
        context.DrawEllipse(fill, ring, new Point(cx, midY), radius, radius);

        if (commit.IsMerge)
        {
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(220, 10, 14, 20)),
                new Pen(Brush(commit.Lane), 1.3),
                new Point(cx, midY), radius * 0.38, radius * 0.38);
        }

        if (isHead)
            context.DrawEllipse(null, new Pen(Brush(commit.Lane), 1.35), new Point(cx, midY), radius + 3.4, radius + 3.4);
    }

    private static double X(int lane) => PadX + lane * Spacing;

    private static void DrawLine(DrawingContext context, Pen pen, Point from, Point to) =>
        context.DrawLine(pen, from, to);

    private static void DrawCurve(DrawingContext context, Pen pen, Point from, Point to)
    {
        if (Math.Abs(from.X - to.X) < 0.4)
        {
            context.DrawLine(pen, from, to);
            return;
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            var dy = to.Y - from.Y;
            g.BeginFigure(from, false);
            g.CubicBezierTo(
                new Point(from.X, from.Y + dy * 0.58),
                new Point(to.X, to.Y - dy * 0.58),
                to);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geo);
    }

    private static Color Lighten(Color c, double amount) =>
        Color.FromRgb(
            (byte)Math.Min(255, c.R + (255 - c.R) * amount),
            (byte)Math.Min(255, c.G + (255 - c.G) * amount),
            (byte)Math.Min(255, c.B + (255 - c.B) * amount));

    private static Color Darken(Color c, double amount) =>
        Color.FromRgb(
            (byte)(c.R * (1 - amount)),
            (byte)(c.G * (1 - amount)),
            (byte)(c.B * (1 - amount)));
}
