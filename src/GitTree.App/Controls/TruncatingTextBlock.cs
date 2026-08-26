using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace GitTree.App.Controls;

public sealed class TruncatingTextBlock : TextBlock
{
    public TruncatingTextBlock()
    {
        TextTrimming = TextTrimming.CharacterEllipsis;
        TextWrapping = TextWrapping.NoWrap;
        Background = Brushes.Transparent;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        ToolTip.SetShowDelay(this, 250);
        ToolTip.SetPlacement(this, PlacementMode.Pointer);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
            ApplyTip(Text);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        ApplyTip(Text);
        base.OnPointerEntered(e);
    }

    private void ApplyTip(string? text)
    {
        ToolTip.SetTip(this, string.IsNullOrWhiteSpace(text) ? null : text);
    }
}
