using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using GitTree.Core;

namespace GitTree.App.Controls;

public sealed class DiffLineView : Grid
{
    public static readonly StyledProperty<DiffLine?> LineProperty =
        AvaloniaProperty.Register<DiffLineView, DiffLine?>(nameof(Line));

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New, monospace");
    private static readonly SolidColorBrush AddedBg = Brush("#163A2A");
    private static readonly SolidColorBrush RemovedBg = Brush("#3A1822");
    private static readonly SolidColorBrush HunkBg = Brush("#121A2C");
    private static readonly SolidColorBrush ContextBg = Brush("#0C1018");
    private static readonly SolidColorBrush AddedFg = Brush("#C6F6DC");
    private static readonly SolidColorBrush RemovedFg = Brush("#FFC9D2");
    private static readonly SolidColorBrush ContextFg = Brush("#D5DCE8");
    private static readonly SolidColorBrush HunkFg = Brush("#9BB4F0");
    private static readonly SolidColorBrush AddedNum = Brush("#5FBF94");
    private static readonly SolidColorBrush RemovedNum = Brush("#E07A8C");
    private static readonly SolidColorBrush MuteNum = Brush("#4A5568");
    private static readonly SolidColorBrush AddedMark = Brush("#3DDC97");
    private static readonly SolidColorBrush RemovedMark = Brush("#FF5C7A");
    private static readonly SolidColorBrush AddedSpan = Brush("#2A6B4A");
    private static readonly SolidColorBrush RemovedSpan = Brush("#7A2C3A");

    static DiffLineView()
    {
        LineProperty.Changed.AddClassHandler<DiffLineView>((view, _) => view.Rebuild());
    }

    public DiffLineView()
    {
        ColumnDefinitions.Add(new ColumnDefinition(46, GridUnitType.Pixel));
        ColumnDefinitions.Add(new ColumnDefinition(46, GridUnitType.Pixel));
        ColumnDefinitions.Add(new ColumnDefinition(20, GridUnitType.Pixel));
        ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        MinHeight = 24;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public DiffLine? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    private void Rebuild()
    {
        Children.Clear();
        var line = Line;
        if (line is null)
            return;

        Background = line.Kind switch
        {
            DiffLineKind.Added => AddedBg,
            DiffLineKind.Removed => RemovedBg,
            DiffLineKind.Hunk => HunkBg,
            _ => ContextBg
        };

        if (line.Kind == DiffLineKind.Hunk)
        {
            var hunk = Text(line.DisplayText, HunkFg, 12, FontWeight.SemiBold);
            hunk.Margin = new Thickness(12, 5, 10, 5);
            hunk.TextWrapping = TextWrapping.Wrap;
            SetColumnSpan(hunk, 4);
            Children.Add(hunk);
            return;
        }

        var oldFg = line.Kind == DiffLineKind.Removed ? RemovedNum : MuteNum;
        var newFg = line.Kind == DiffLineKind.Added ? AddedNum : MuteNum;
        var prefixFg = line.Kind switch
        {
            DiffLineKind.Added => AddedMark,
            DiffLineKind.Removed => RemovedMark,
            _ => MuteNum
        };
        var textFg = line.Kind switch
        {
            DiffLineKind.Added => AddedFg,
            DiffLineKind.Removed => RemovedFg,
            _ => ContextFg
        };

        Children.Add(Gutter(line.OldNumberText, oldFg, 0));
        Children.Add(Gutter(line.NewNumberText, newFg, 1));

        var prefix = Text(line.Prefix, prefixFg, 13, FontWeight.Bold);
        prefix.HorizontalAlignment = HorizontalAlignment.Center;
        prefix.VerticalAlignment = VerticalAlignment.Top;
        prefix.Margin = new Thickness(0, 3, 0, 0);
        SetColumn(prefix, 2);
        Children.Add(prefix);

        var body = new TextBlock
        {
            FontFamily = Mono,
            FontSize = 13,
            Foreground = textFg,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 3, 12, 3),
            VerticalAlignment = VerticalAlignment.Top
        };
        if (line.Segments.Count > 0)
        {
            var highlight = line.Kind == DiffLineKind.Added ? AddedSpan : RemovedSpan;
            var inlines = new InlineCollection();
            foreach (var segment in line.Segments)
            {
                inlines.Add(new Run(segment.Text)
                {
                    Background = segment.Highlight ? highlight : null,
                    Foreground = textFg
                });
            }

            body.Inlines = inlines;
        }
        else
        {
            body.Text = line.DisplayText;
        }

        SetColumn(body, 3);
        Children.Add(body);
    }

    private static TextBlock Gutter(string text, IBrush foreground, int column)
    {
        var block = Text(text, foreground, 11, FontWeight.Normal);
        block.HorizontalAlignment = HorizontalAlignment.Right;
        block.VerticalAlignment = VerticalAlignment.Top;
        block.Margin = new Thickness(0, 4, 8, 0);
        SetColumn(block, column);
        return block;
    }

    private static TextBlock Text(string text, IBrush foreground, double size, FontWeight weight) =>
        new()
        {
            Text = text,
            Foreground = foreground,
            FontFamily = Mono,
            FontSize = size,
            FontWeight = weight
        };

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
