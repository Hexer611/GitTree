using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GitTree.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
