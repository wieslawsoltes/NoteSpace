using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NoteSpace.Core;
using Windows.System;

namespace NoteSpace.Controls;

/// <summary>Host-independent formatting toolbar. It raises existing command IDs and
/// never owns or changes a text buffer. Hosts decide when and where it is displayed.</summary>
public sealed class SelectionToolbar : UserControl
{
    public const float ToolbarWidth = 260;
    public const float ToolbarHeight = 40;
    private readonly Border frame = new() { CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), Padding = new Thickness(5, 3, 5, 3) };
    private readonly StackPanel buttons = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, OfficeButton> controls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MenuFlyoutItem> menuItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (bool Selected, bool Enabled)> states = new(StringComparer.Ordinal);
    private OfficeTheme? palette;
    private string? fontCaption;
    private bool menuOpen;
    public bool IsMenuOpen => menuOpen;
    public event EventHandler<string>? CommandInvoked;
    public event EventHandler? DismissRequested;
    public SelectionToolbar()
    {
        Width = ToolbarWidth; Height = ToolbarHeight;
        frame.Child = buttons; Content = frame;
        AutomationProperties.SetAutomationId(this, "selection-toolbar");
        AutomationProperties.SetName(this, "Selection formatting");
        KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { DismissRequested?.Invoke(this, EventArgs.Empty); e.Handled = true; } };
        SetTheme(OfficeTheme.Light);
    }
    public void SetTheme(OfficeTheme theme)
    {
        if (palette == theme) return;
        palette = theme; buttons.Children.Clear(); controls.Clear(); menuItems.Clear(); fontCaption = null;
        frame.Background = OfficeTheme.Brush(theme.Surface); frame.BorderBrush = OfficeTheme.Brush(OfficeTheme.Accent);
        foreach (var (id, label, icon) in new[] {
            ("font", "Font and size", ""), ("bold", "Bold", "B"), ("italic", "Italic", "I"),
            ("underline", "Underline", "U"), ("text-highlight", "Highlight", "highlighter") })
        {
            var button = new OfficeButton(id == "font" ? "Font / size" : "", icon, () => CommandInvoked?.Invoke(this, id), label, theme: theme)
                { Width = id == "font" ? 88 : 32, Height = 32, Padding = new Thickness(3) };
            AutomationProperties.SetAutomationId(button, "mini-" + id); controls.Add(id, button); buttons.Children.Add(button);
        }
        var more = new OfficeButton("…", "", () => { }, "More selection formatting", theme: theme) { Width = 32, Height = 32, Padding = new Thickness(3) };
        var menu = new MenuFlyout();
        menu.Opened += (_, _) => menuOpen = true; menu.Closed += (_, _) => menuOpen = false;
        foreach (var (id, label) in new[] {
            ("strike", "Strikethrough"), ("superscript", "Superscript"), ("subscript", "Subscript"),
            ("text-color", "Font color"), ("clear-format", "Clear formatting"), ("copy-format", "Copy format"),
            ("paste-format", "Paste format"), ("copy", "Copy selected text"), ("cut", "Cut selected text") })
        {
            var item = new MenuFlyoutItem { Text = label };
            AutomationProperties.SetAutomationId(item, "mini-" + id);
            item.Click += (_, _) => CommandInvoked?.Invoke(this, id); menu.Items.Add(item); menuItems[id] = item;
        }
        more.Flyout = menu; AutomationProperties.SetAutomationId(more, "mini-more"); buttons.Children.Add(more);
        foreach (var state in states) PaintState(state.Key, state.Value.Selected, state.Value.Enabled);
    }
    public void SetFont(TextFormat format)
    {
        var caption = format.FontSize.ToString("0.#", CultureInfo.InvariantCulture) + " · " + format.FontFamily;
        if (caption == fontCaption || palette is null) return;
        fontCaption = caption;
        var label = palette.Label(caption, 11); label.MaxWidth = 80;
        controls["font"].Content = label;
        AutomationProperties.SetName(controls["font"], "Font and size: " + caption);
    }
    public void SetCommandState(string id, bool selected, bool enabled = true)
    {
        if (states.TryGetValue(id, out var old) && old == (selected, enabled)) return;
        states[id] = (selected, enabled); PaintState(id, selected, enabled);
    }
    private void PaintState(string id, bool selected, bool enabled)
    {
        if (controls.TryGetValue(id, out var control)) { control.Selected = selected; control.IsEnabled = enabled; }
        if (menuItems.TryGetValue(id, out var item)) { item.IsEnabled = enabled; item.Icon = selected ? new SymbolIcon(Symbol.Accept) : null; }
    }
    public void FocusFirst() => controls["font"].Focus(FocusState.Keyboard);
}
