using Avalonia.Data.Converters;
using Avalonia.Media;
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
            DiffLineKind.Added => new SolidColorBrush(Color.Parse("#163A2A")),
            DiffLineKind.Removed => new SolidColorBrush(Color.Parse("#3A1822")),
            DiffLineKind.Hunk => new SolidColorBrush(Color.Parse("#121A2C")),
            DiffLineKind.Meta => new SolidColorBrush(Color.Parse("#141820")),
            _ => new SolidColorBrush(Color.Parse("#0C1018"))
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
            DiffLineKind.Added => new SolidColorBrush(Color.Parse("#3DDC97")),
            DiffLineKind.Removed => new SolidColorBrush(Color.Parse("#FF5C7A")),
            DiffLineKind.Hunk => new SolidColorBrush(Color.Parse("#5B8DEF")),
            DiffLineKind.Meta => new SolidColorBrush(Color.Parse("#6B7385")),
            _ => new SolidColorBrush(Color.Parse("#1E2430"))
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
            "fg" or "path" => new SolidColorBrush(Foreground(badge)),
            _ => new SolidColorBrush(Background(badge))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Color Foreground(string badge) => badge switch
    {
        "A" or "?" => Color.Parse("#7EB0FF"),
        "D" => Color.Parse("#FF7A90"),
        "C" => Color.Parse("#FFFFFF"),
        "R" or "P" => Color.Parse("#8BB0FF"),
        _ => Color.Parse("#F5C542")
    };

    private static Color Background(string badge) => badge switch
    {
        "A" or "?" => Color.Parse("#335B8DEF"),
        "D" => Color.Parse("#33FF5C7A"),
        "C" => Color.Parse("#9B1B32"),
        "R" or "P" => Color.Parse("#285B8DEF"),
        _ => Color.Parse("#33F5C542")
    };
}

public sealed class ConflictRowBrushConverter : IValueConverter
{
    public static readonly ConflictRowBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
            return new SolidColorBrush(Color.Parse("#FF5C7A"));
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
            return new SolidColorBrush(Color.Parse("#14080C"));
        if (value is FileChange file)
            return FileBadgeBrushConverter.Instance.Convert(file.BadgeChar, targetType, "path", culture);
        return new SolidColorBrush(Color.Parse("#E7ECF5"));
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
        return new SolidColorBrush(_part switch
        {
            "bg" => bg,
            "border" => border,
            _ => fg
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static (Color Fg, Color Bg, Color Border) Palette(string label)
    {
        if (label.Equals("HEAD", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("HEAD", StringComparison.OrdinalIgnoreCase))
            return (Color.Parse("#8FF5C6"), Color.Parse("#333DDC97"), Color.Parse("#663DDC97"));
        if (label.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            return (Color.Parse("#F5C542"), Color.Parse("#28F5C542"), Color.Parse("#55F5C542"));
        if (label.Contains('/'))
            return (Color.Parse("#9BB4FF"), Color.Parse("#285B8DEF"), Color.Parse("#445B8DEF"));
        return (Color.Parse("#8FF5C6"), Color.Parse("#1A3DDC97"), Color.Parse("#333DDC97"));
    }
}
