using Avalonia;
using Avalonia.Media;

namespace GitTree.App.Theming;

public static class ThemeResources
{
    public static IBrush Brush(string key)
    {
        var app = Application.Current;
        if (app is not null
            && app.TryGetResource(key, app.ActualThemeVariant, out var value)
            && value is IBrush brush)
            return brush;

        return Brushes.Transparent;
    }
}
