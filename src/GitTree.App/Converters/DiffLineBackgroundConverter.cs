using Avalonia.Data.Converters;
using Avalonia.Media;
using GitTree.App.Theming;
using GitTree.Core;
using System.Globalization;

namespace GitTree.App.Converters;

public sealed class DiffLineBackgroundConverter : IValueConverter
{
    public static readonly DiffLineBackgroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DiffLineKind.Added => ThemeResources.Brush("DiffAddedBgBrush"),
            DiffLineKind.Removed => ThemeResources.Brush("DiffRemovedBgBrush"),
            DiffLineKind.Hunk => ThemeResources.Brush("DiffHunkBgBrush"),
            DiffLineKind.Meta => ThemeResources.Brush("Bg2Brush"),
            _ => ThemeResources.Brush("DiffBgBrush")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DiffLineGutterConverter : IValueConverter
{
    public static readonly DiffLineGutterConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            DiffLineKind.Added => ThemeResources.Brush("AccentBrush"),
            DiffLineKind.Removed => ThemeResources.Brush("DangerBrush"),
            DiffLineKind.Hunk => ThemeResources.Brush("InfoBrush"),
            DiffLineKind.Meta => ThemeResources.Brush("MutedBrush"),
            _ => ThemeResources.Brush("LineBrush")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class FileBadgeBrushConverter : IValueConverter
{
    public static readonly FileBadgeBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var badge = value as string ?? "M";
        var part = parameter as string ?? "bg";
        return part switch
        {
            "fg" or "path" => Foreground(badge),
            _ => Background(badge)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static IBrush Foreground(string badge) => badge switch
    {
        "A" or "?" => ThemeResources.Brush("InfoBrush"),
        "D" => ThemeResources.Brush("DangerBrush"),
        "C" => ThemeResources.Brush("TextBrush"),
        "R" or "P" => ThemeResources.Brush("InfoBrush"),
        _ => ThemeResources.Brush("WarningBrush")
    };

    private static IBrush Background(string badge) => badge switch
    {
        "A" or "?" => ThemeResources.Brush("InfoSubtleBrush"),
        "D" => ThemeResources.Brush("DangerSubtleBrush"),
        "C" => ThemeResources.Brush("DangerBrush"),
        "R" or "P" => ThemeResources.Brush("InfoSubtleBrush"),
        _ => ThemeResources.Brush("WarningSubtleBrush")
    };
}

public sealed class ConflictRowBrushConverter : IValueConverter
{
    public static readonly ConflictRowBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return ThemeResources.Brush("DangerBrush");
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConflictTextBrushConverter : IValueConverter
{
    public static readonly ConflictTextBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is FileChange { IsConflict: true })
            return ThemeResources.Brush("AccentOnBrush");
        if (value is FileChange file)
            return FileBadgeBrushConverter.Instance.Convert(file.BadgeChar, targetType, "path", culture);
        return ThemeResources.Brush("TextBrush");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DecorationChipConverter : IValueConverter
{
    public static readonly DecorationChipConverter Background = new("bg");
    public static readonly DecorationChipConverter Border = new("border");
    public static readonly DecorationChipConverter Foreground = new("fg");

    private readonly string _part;
    private DecorationChipConverter(string part) => _part = part;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var label = value as string ?? "";
        var (fg, bg, border) = Palette(label);
        return _part switch
        {
            "bg" => bg,
            "border" => border,
            _ => fg
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static (IBrush Fg, IBrush Bg, IBrush Border) Palette(string label)
    {
        if (label.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("HEAD", StringComparison.OrdinalIgnoreCase))
            return (ThemeResources.Brush("AccentBrush"), ThemeResources.Brush("AccentSoftBrush"), ThemeResources.Brush("AccentBorderBrush"));
        if (label.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            return (ThemeResources.Brush("WarningBrush"), ThemeResources.Brush("WarningSubtleBrush"), ThemeResources.Brush("WarningBrush"));
        if (label.Contains('/'))
            return (ThemeResources.Brush("InfoBrush"), ThemeResources.Brush("InfoSubtleBrush"), ThemeResources.Brush("InfoBrush"));
        return (ThemeResources.Brush("AccentBrush"), ThemeResources.Brush("AccentSubtleBrush"), ThemeResources.Brush("AccentSoftBrush"));
    }
}
