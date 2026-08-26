using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using GitTree.Core;

namespace GitTree.App.Theming;

public static class ThemeManager
{
    public static AppTheme Current { get; private set; } = ThemeCatalog.Default;

    public static void ApplySaved() => Apply(ThemeCatalog.Find(new SettingsStore().Load().ThemeId), persist: false);

    public static void Apply(AppTheme theme, bool persist = true)
    {
        Current = theme;
        var app = Application.Current;
        if (app is null)
            return;

        app.RequestedThemeVariant = theme.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;
        var r = app.Resources;

        Set(r, "Bg0", theme.Bg0);
        Set(r, "Bg1", theme.Bg1);
        Set(r, "Bg2", theme.Bg2);
        Set(r, "Bg3", theme.Bg3);
        Set(r, "Line", theme.Line);
        Set(r, "Text", theme.Text);
        Set(r, "Muted", theme.Muted);
        Set(r, "Accent", theme.Accent);
        Set(r, "AccentHover", theme.AccentHover);
        Set(r, "AccentBorder", theme.AccentBorder);
        Set(r, "AccentOn", theme.AccentOn);
        Set(r, "AccentDim", theme.AccentDim);
        Set(r, "Danger", theme.Danger);
        Set(r, "Warning", theme.Warning);
        Set(r, "Info", theme.Info);
        Set(r, "InputBg", theme.InputBg);
        Set(r, "Toolbar", theme.Toolbar);
        Set(r, "Overlay", theme.Overlay);
        Set(r, "AccentSubtle", WithAlpha(theme.Accent, 0x1A));
        Set(r, "AccentSoft", WithAlpha(theme.Accent, 0x33));
        Set(r, "WarningSubtle", WithAlpha(theme.Warning, 0x28));
        Set(r, "InfoSubtle", WithAlpha(theme.Info, 0x28));
        Set(r, "DangerSubtle", WithAlpha(theme.Danger, 0x33));
        Set(r, "DiffBg", theme.InputBg);
        Set(r, "DiffAddedBg", Mix(theme.InputBg, theme.Accent, 0.22));
        Set(r, "DiffRemovedBg", Mix(theme.InputBg, theme.Danger, 0.22));
        Set(r, "DiffHunkBg", Mix(theme.InputBg, theme.Info, 0.16));
        Set(r, "DiffAddedFg", Mix(theme.Text, theme.Accent, 0.35));
        Set(r, "DiffRemovedFg", Mix(theme.Text, theme.Danger, 0.35));
        Set(r, "DiffAddedSpan", Mix(theme.InputBg, theme.Accent, 0.42));
        Set(r, "DiffRemovedSpan", Mix(theme.InputBg, theme.Danger, 0.42));
        SetNamedBrush(r, "ExpanderHeaderBackground", theme.Bg2);
        SetNamedBrush(r, "ExpanderHeaderForeground", theme.Text);
        SetNamedBrush(r, "ExpanderHeaderBorderBrush", theme.Line);
        SetNamedBrush(r, "ExpanderHeaderPointerOverBackground", theme.Bg3);
        SetNamedBrush(r, "ExpanderHeaderPressedBackground", theme.Bg3);
        SetNamedBrush(r, "ExpanderHeaderDisabledBackground", theme.Bg1);
        SetNamedBrush(r, "ExpanderContentBackground", theme.Bg1);
        SetNamedBrush(r, "ExpanderContentBorderBrush", theme.Line);
        SetNamedBrush(r, "ExpanderChevronForeground", theme.Muted);
        SetNamedBrush(r, "ExpanderChevronPointerOverForeground", theme.Text);
        SetNamedBrush(r, "ExpanderChevronBackground", Colors.Transparent);
        SetNamedBrush(r, "ExpanderChevronPointerOverBackground", theme.Overlay);

        if (persist)
            new SettingsStore().Update(s => s.ThemeId = theme.Id);

        Changed?.Invoke();
    }

    public static event Action? Changed;

    private static void SetNamedBrush(IResourceDictionary resources, string key, Color color)
    {
        if (resources.TryGetValue(key, out var existing) && existing is SolidColorBrush brush)
            brush.Color = color;
        else
            resources[key] = new SolidColorBrush(color);
    }

    private static void Set(IResourceDictionary resources, string key, Color color)
    {
        resources[key] = color;
        var brushKey = key + "Brush";
        if (resources.TryGetValue(brushKey, out var existing) && existing is SolidColorBrush brush)
            brush.Color = color;
        else
            resources[brushKey] = new SolidColorBrush(color);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);
}
