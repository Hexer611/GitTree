using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using GitTree.App.Theming;
using GitTree.Core;

namespace GitTree.App.Controls;

public sealed class DiffLineView : Grid
{
    public static readonly StyledProperty<DiffLine?> LineProperty =
        AvaloniaProperty.Register<DiffLineView, DiffLine?>(nameof(Line));

    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New, monospace");

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
        ThemeManager.Changed += OnThemeChanged;
        DetachedFromVisualTree += (_, _) => ThemeManager.Changed -= OnThemeChanged;
    }

    public DiffLine? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    private void OnThemeChanged() => Rebuild();

    private void Rebuild()
    {
        Children.Clear();
        var line = Line;
        if (line is null)
            return;

        var addedBg = ThemeResources.Brush("DiffAddedBgBrush");
        var removedBg = ThemeResources.Brush("DiffRemovedBgBrush");
        var hunkBg = ThemeResources.Brush("DiffHunkBgBrush");
        var contextBg = ThemeResources.Brush("DiffBgBrush");
        var addedFg = ThemeResources.Brush("DiffAddedFgBrush");
        var removedFg = ThemeResources.Brush("DiffRemovedFgBrush");
        var contextFg = ThemeResources.Brush("TextBrush");
        var hunkFg = ThemeResources.Brush("InfoBrush");
        var addedMark = ThemeResources.Brush("AccentBrush");
        var removedMark = ThemeResources.Brush("DangerBrush");
        var mute = ThemeResources.Brush("MutedBrush");
        var addedSpan = ThemeResources.Brush("DiffAddedSpanBrush");
        var removedSpan = ThemeResources.Brush("DiffRemovedSpanBrush");

        Background = line.Kind switch
        {
            DiffLineKind.Added => addedBg,
            DiffLineKind.Removed => removedBg,
            DiffLineKind.Hunk => hunkBg,
            _ => contextBg
        };

        if (line.Kind == DiffLineKind.Hunk)
        {
            var hunk = Text(line.DisplayText, hunkFg, 12, FontWeight.SemiBold);
            hunk.Margin = new Thickness(12, 5, 10, 5);
            hunk.TextWrapping = TextWrapping.Wrap;
            SetColumnSpan(hunk, 4);
            Children.Add(hunk);
            return;
        }

        var oldFg = line.Kind == DiffLineKind.Removed ? removedMark : mute;
        var newFg = line.Kind == DiffLineKind.Added ? addedMark : mute;
        var prefixFg = line.Kind switch
        {
            DiffLineKind.Added => addedMark,
            DiffLineKind.Removed => removedMark,
            _ => mute
        };
        var textFg = line.Kind switch
        {
            DiffLineKind.Added => addedFg,
            DiffLineKind.Removed => removedFg,
            _ => contextFg
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
            var highlight = line.Kind == DiffLineKind.Added ? addedSpan : removedSpan;
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
}
