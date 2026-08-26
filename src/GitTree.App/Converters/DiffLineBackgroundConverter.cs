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
            DiffLineKind.Added => new SolidColorBrush(Color.FromArgb(160, 27, 94, 32)),
            DiffLineKind.Removed => new SolidColorBrush(Color.FromArgb(150, 183, 28, 28)),
            DiffLineKind.Hunk => new SolidColorBrush(Color.FromArgb(140, 21, 101, 192)),
            DiffLineKind.Meta => new SolidColorBrush(Color.FromArgb(140, 55, 71, 79)),
            _ => Brushes.Transparent
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
