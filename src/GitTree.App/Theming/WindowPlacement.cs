using Avalonia;
using Avalonia.Controls;
using GitTree.App.Views;
using GitTree.Core;

namespace GitTree.App.Theming;

public static class WindowPlacement
{
    public static void Restore(Window window)
    {
        var settings = new SettingsStore().Load();
        if (!settings.RememberWindowPosition)
            return;

        if (settings.WindowWidth >= window.MinWidth)
            window.Width = settings.WindowWidth;
        if (settings.WindowHeight >= window.MinHeight)
            window.Height = settings.WindowHeight;

        if (settings.WindowX != int.MinValue && settings.WindowY != int.MinValue)
            window.Position = new PixelPoint(settings.WindowX, settings.WindowY);

        if (string.Equals(settings.WindowState, nameof(WindowState.Maximized), StringComparison.OrdinalIgnoreCase))
            window.WindowState = WindowState.Maximized;

        if (window is MainWindow main)
            RestorePanes(main, settings);
    }

    public static void RestorePanes(MainWindow window)
    {
        var settings = new SettingsStore().Load();
        if (!settings.RememberWindowPosition)
            return;
        RestorePanes(window, settings);
    }

    public static void Save(Window window)
    {
        var settings = new SettingsStore().Load();
        if (!settings.RememberWindowPosition)
            return;

        var state = window.WindowState;
        settings.WindowState = state.ToString();
        if (state == WindowState.Normal)
        {
            settings.WindowX = window.Position.X;
            settings.WindowY = window.Position.Y;
            settings.WindowWidth = window.Width;
            settings.WindowHeight = window.Height;
        }

        if (window is MainWindow main)
            CapturePanes(main, settings);

        new SettingsStore().Save(settings);
    }

    private static void CapturePanes(MainWindow window, AppSettings settings)
    {
        var workspace = window.FindControl<Grid>("WorkspaceGrid");
        if (workspace?.ColumnDefinitions is { Count: >= 3 } cols
            && TrySize(cols[0], out var sidebar) && sidebar >= 160)
            settings.SidebarWidth = sidebar;

        var graphDiff = window.FindControl<Grid>("GraphDiffGrid");
        if (graphDiff?.ColumnDefinitions is { Count: >= 3 } graphCols
            && TrySize(graphCols[0], out var graphW)
            && TrySize(graphCols[2], out var diffW)
            && graphW + diffW > 1)
            settings.GraphVsDiffShare = ClampShare(graphW / (graphW + diffW));

        var graphFiles = window.FindControl<Grid>("GraphFilesGrid");
        if (graphFiles?.RowDefinitions is { Count: >= 3 } rows
            && TrySize(rows[0], out var graphH)
            && TrySize(rows[2], out var filesH)
            && graphH + filesH > 1)
            settings.GraphVsFilesShare = ClampShare(graphH / (graphH + filesH));
    }

    private static void RestorePanes(MainWindow window, AppSettings settings)
    {
        var workspace = window.FindControl<Grid>("WorkspaceGrid");
        if (workspace?.ColumnDefinitions is { Count: >= 3 } && settings.SidebarWidth >= 160)
            workspace.ColumnDefinitions[0].Width = new GridLength(settings.SidebarWidth);

        var graphDiff = window.FindControl<Grid>("GraphDiffGrid");
        if (graphDiff?.ColumnDefinitions is { Count: >= 3 } && settings.GraphVsDiffShare is > 0 and < 1)
        {
            graphDiff.ColumnDefinitions[0].Width = new GridLength(settings.GraphVsDiffShare, GridUnitType.Star);
            graphDiff.ColumnDefinitions[2].Width = new GridLength(1 - settings.GraphVsDiffShare, GridUnitType.Star);
        }

        var graphFiles = window.FindControl<Grid>("GraphFilesGrid");
        if (graphFiles?.RowDefinitions is { Count: >= 3 } && settings.GraphVsFilesShare is > 0 and < 1)
        {
            graphFiles.RowDefinitions[0].Height = new GridLength(settings.GraphVsFilesShare, GridUnitType.Star);
            graphFiles.RowDefinitions[2].Height = new GridLength(1 - settings.GraphVsFilesShare, GridUnitType.Star);
        }
    }

    private static bool TrySize(DefinitionBase definition, out double size)
    {
        size = definition switch
        {
            ColumnDefinition column => column.ActualWidth,
            RowDefinition row => row.ActualHeight,
            _ => 0
        };
        return size > 1;
    }

    private static double ClampShare(double share) =>
        Math.Clamp(share, 0.18, 0.82);
}
