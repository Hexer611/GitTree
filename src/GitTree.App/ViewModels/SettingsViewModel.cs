using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitTree.App.Theming;
using GitTree.Core;

namespace GitTree.App.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsStore _store = new();

    public IReadOnlyList<AppTheme> Themes { get; } = ThemeCatalog.All;

    [ObservableProperty] private AppTheme _selectedTheme = ThemeManager.Current;
    [ObservableProperty] private bool _rememberWindowPosition = true;

    public SettingsViewModel()
    {
        var settings = _store.Load();
        RememberWindowPosition = settings.RememberWindowPosition;
        SelectedTheme = ThemeCatalog.Find(settings.ThemeId);
    }

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (value is not null)
            ThemeManager.Apply(value);
    }

    partial void OnRememberWindowPositionChanged(bool value) =>
        _store.Update(s => s.RememberWindowPosition = value);

    [RelayCommand]
    private void SelectTheme(AppTheme? theme)
    {
        if (theme is not null)
            SelectedTheme = theme;
    }
}
