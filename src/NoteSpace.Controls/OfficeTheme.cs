using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace NoteSpace.Controls;

public sealed record OfficeTheme(bool IsDark, uint Surface, uint Panel, uint Text, uint Muted, uint Border, uint Hover, uint Selection)
{
    public const uint Accent = 0xFF803AB3;
    public static OfficeTheme Light { get; } = new(false, 0xFFFFFFFF, 0xFFF7F7F7, 0xFF242424, 0xFF707070, 0xFFE2E2E2, 0xFFF0EAF4, 0xFFEFE3F7);
    public static OfficeTheme Dark { get; } = new(true, 0xFF252525, 0xFF202020, 0xFFF2F2F2, 0xFFB2B2B2, 0xFF424242, 0xFF43364B, 0xFF513463);
    public static SolidColorBrush Brush(uint value) => new(Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value));
    public static FontFamily ResolveFont(string family = "Segoe UI") => new(
        OperatingSystem.IsBrowser() && family is "Segoe UI" or "Open Sans" or "Arial" or "Calibri"
            ? "ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans.ttf#Open Sans" : family);
    public TextBlock Label(string text, double size = 13, bool bold = false, uint? color = null) => new()
    {
        Text = text, FontSize = size, FontFamily = ResolveFont(),
        FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(bold ? 600 : 400) },
        Foreground = Brush(color ?? Text), VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis
    };
}

public sealed class OfficeButton : Button
{
    private bool hovered;
    private bool selected;
    private OfficeTheme theme = OfficeTheme.Light;
    public OfficeTheme Theme { get => theme; set { theme = value; Foreground = OfficeTheme.Brush(value.Text); Refresh(); } }
    public bool Selected { get => selected; set { selected = value; Refresh(); } }
    public OfficeButton()
    {
        Padding = new Thickness(7, 4, 7, 4); MinWidth = 0; MinHeight = 0; CornerRadius = new CornerRadius(3); BorderThickness = new Thickness(0); FontSize = 13;
        HorizontalContentAlignment = HorizontalAlignment.Center; VerticalContentAlignment = VerticalAlignment.Center;
        PointerEntered += (_, _) => { hovered = true; Refresh(); };
        PointerExited += (_, _) => { hovered = false; Refresh(); };
        Refresh();
    }
    public OfficeButton(string label, string icon, Action action, string? hint = null, bool vertical = false, OfficeTheme? theme = null) : this()
    {
        Theme = theme ?? OfficeTheme.Light;
        var content = new StackPanel { Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal, Spacing = vertical ? 5 : 7, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        if (!string.IsNullOrEmpty(icon)) content.Children.Add(new NoteIcon { Glyph = icon, Width = vertical ? 27 : 18, Height = vertical ? 27 : 18, InkColor = Theme.Text, HorizontalAlignment = HorizontalAlignment.Center });
        if (!string.IsNullOrEmpty(label)) content.Children.Add(new TextBlock { Text = label, FontSize = vertical ? 11 : 13, TextAlignment = TextAlignment.Center, Foreground = OfficeTheme.Brush(Theme.Text), VerticalAlignment = VerticalAlignment.Center });
        Content = content;
        AutomationProperties.SetName(this, hint ?? label);
        ToolTipService.SetToolTip(this, hint ?? label);
        Click += (_, _) => action();
    }
    private void Refresh() => Background = OfficeTheme.Brush(selected ? theme.Selection : hovered ? theme.Hover : 0x00000000);
}

public sealed record CommandRequest(string CommandId, string? EntityId = null);
public static class OfficeMenus
{
    public static MenuFlyout Create(Action<string> action, params (string Id, string Label)[] items)
    {
        var menu = new MenuFlyout();
        foreach (var (id, label) in items)
        {
            if (id == "-") { menu.Items.Add(new MenuFlyoutSeparator()); continue; }
            var item = new MenuFlyoutItem { Text = label }; item.Click += (_, _) => action(id); menu.Items.Add(item);
        }
        return menu;
    }
}
