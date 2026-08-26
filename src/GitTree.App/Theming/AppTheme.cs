using Avalonia.Media;

namespace GitTree.App.Theming;

public sealed class AppTheme
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public bool IsLight { get; init; }
    public bool IsHighContrast { get; init; }

    public string AppearanceTag => IsLight ? "Light" : "Dark";
    public string ContrastTag => IsHighContrast ? "High contrast" : "Low contrast";

    public required Color Bg0 { get; init; }
    public required Color Bg1 { get; init; }
    public required Color Bg2 { get; init; }
    public required Color Bg3 { get; init; }
    public required Color Line { get; init; }
    public required Color Text { get; init; }
    public required Color Muted { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentHover { get; init; }
    public required Color AccentBorder { get; init; }
    public required Color AccentOn { get; init; }
    public required Color AccentDim { get; init; }
    public required Color Danger { get; init; }
    public required Color Warning { get; init; }
    public required Color Info { get; init; }
    public required Color InputBg { get; init; }
    public required Color Toolbar { get; init; }
    public required Color Overlay { get; init; }

    public IBrush Bg0Brush => new SolidColorBrush(Bg0);
    public IBrush Bg1Brush => new SolidColorBrush(Bg1);
    public IBrush AccentBrush => new SolidColorBrush(Accent);
    public IBrush LineBrush => new SolidColorBrush(Line);
}
