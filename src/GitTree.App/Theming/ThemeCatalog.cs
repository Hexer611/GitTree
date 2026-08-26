using Avalonia.Media;

namespace GitTree.App.Theming;

public static class ThemeCatalog
{
    public static IReadOnlyList<AppTheme> All { get; } =
    [
        Make("nord", "Nord", "Cool slate with polar, easy-on-the-eyes light.", light: false, highContrast: false,
            "#2B303B", "#323844", "#3A4150", "#434B5A", "#4F5868", "#D0D6E0", "#8E98A8",
            "#7EADC0", "#8EB8C8", "#9AC0CE", "#2B303B", "#4E7380",
            "#B88890", "#C0B080", "#7E98B0", "#2E3440", "#E6323844", "#0EFFFFFF"),
        Make("dusk", "Dusk", "Warm, low-glare dark palette made for long sessions.", light: false, highContrast: false,
            "#1A1916", "#22201C", "#2A2722", "#322E28", "#3F3A33", "#D4CDBF", "#9A9286",
            "#8AA67A", "#9CB68C", "#A8C098", "#1A1916", "#5C7354",
            "#C47A7A", "#C4A66A", "#7A9AB0", "#1E1C18", "#E622201C", "#10FFFFFF"),
        Make("forest", "Forest", "The original mint-on-night look.", light: false, highContrast: true,
            "#07090D", "#0C1018", "#121826", "#171F30", "#243044", "#E7ECF5", "#8B95A8",
            "#3DDC97", "#63F0B2", "#6AF0B6", "#07110C", "#1B8F62",
            "#FF5C7A", "#F5C542", "#5B8DEF", "#0A0E16", "#CC0C1018", "#14FFFFFF"),
        Make("midnight", "Midnight", "Soft navy with quiet ice-blue accents.", light: false, highContrast: false,
            "#161A22", "#1C222C", "#232A36", "#2A3240", "#3C4656", "#C8D0DC", "#8A94A4",
            "#7A9BB8", "#8AABC4", "#96B4C8", "#161A22", "#4A6880",
            "#C09098", "#C0A878", "#7A9BB8", "#181E28", "#E61C222C", "#0EFFFFFF"),
        Make("ember", "Ember", "Warm charcoal with gentle copper light.", light: false, highContrast: false,
            "#1C1814", "#241E1A", "#2C2620", "#342E26", "#4A4036", "#E0D4C8", "#A09080",
            "#C09A78", "#C8A888", "#D0B090", "#1C1814", "#8A6A4A",
            "#C09088", "#C0A878", "#B09078", "#201C18", "#E6241E1A", "#0EFFFFFF"),
        Make("orchid", "Orchid", "Muted violet dusk with lavender accents.", light: false, highContrast: false,
            "#18161E", "#1E1C26", "#262430", "#2E2A38", "#443E50", "#D8D0E0", "#988EA8",
            "#A090B8", "#B0A0C4", "#B8A8CC", "#18161E", "#6A6080",
            "#C090A0", "#C0A878", "#9090B8", "#1C1A24", "#E61E1C26", "#0EFFFFFF"),
        Make("paper", "Paper", "Warm cream workspace with soft ink contrast.", light: true, highContrast: false,
            "#E8E0D2", "#F2EBE0", "#E6DDD0", "#DED4C4", "#C8BCA8", "#3E382E", "#7A7266",
            "#5A8A6A", "#4E7A5E", "#447054", "#F2EBE0", "#4A7358",
            "#A86868", "#A08040", "#5A7A98", "#F6F0E6", "#E6F2EBE0", "#0A000000"),
        Make("obsidian", "Obsidian", "High-contrast black with sharp lime.", light: false, highContrast: true,
            "#000000", "#0A0A0A", "#141414", "#1C1C1C", "#3A3A3A", "#F5F5F5", "#9A9A9A",
            "#C8FF00", "#DCFF4A", "#E8FF7A", "#101400", "#6A8800",
            "#FF3355", "#FFD000", "#4DA3FF", "#050505", "#CC0A0A0A", "#22FFFFFF"),
    ];

    public static AppTheme Default => All[0];

    public static AppTheme Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Default;

    private static AppTheme Make(
        string id, string name, string description, bool light, bool highContrast,
        string bg0, string bg1, string bg2, string bg3, string line, string text, string muted,
        string accent, string accentHover, string accentBorder, string accentOn, string accentDim,
        string danger, string warning, string info, string inputBg, string toolbar, string overlay) =>
        new()
        {
            Id = id,
            Name = name,
            Description = description,
            IsLight = light,
            IsHighContrast = highContrast,
            Bg0 = C(bg0),
            Bg1 = C(bg1),
            Bg2 = C(bg2),
            Bg3 = C(bg3),
            Line = C(line),
            Text = C(text),
            Muted = C(muted),
            Accent = C(accent),
            AccentHover = C(accentHover),
            AccentBorder = C(accentBorder),
            AccentOn = C(accentOn),
            AccentDim = C(accentDim),
            Danger = C(danger),
            Warning = C(warning),
            Info = C(info),
            InputBg = C(inputBg),
            Toolbar = C(toolbar),
            Overlay = C(overlay)
        };

    private static Color C(string hex) => Color.Parse(hex);
}
