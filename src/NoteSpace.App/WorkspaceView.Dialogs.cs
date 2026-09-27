using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Controls;
using NoteSpace.Core;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private readonly SemaphoreSlim dialogGate = new(1, 1);
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        await dialogGate.WaitAsync();
        try { dialog.XamlRoot = XamlRoot; dialog.RequestedTheme = RequestedTheme; return await dialog.ShowAsync(); }
        finally { dialogGate.Release(); }
    }
    private async Task MessageAsync(string heading, string text)
    {
        await ShowDialogAsync(new ContentDialog { Title = heading, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 }, CloseButtonText = "Close" });
    }
    private async Task<bool> ConfirmAsync(string heading, string text, string accept = "Continue") =>
        await ShowDialogAsync(new ContentDialog { Title = heading, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 580 }, PrimaryButtonText = accept, CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }) == ContentDialogResult.Primary;
    private async Task<string?> PromptAsync(string heading, string value, string? placeholder = null)
    {
        var input = new TextBox { Text = value, PlaceholderText = placeholder ?? "", MinWidth = 360, MaxWidth = 640, MaxLength = 100000 };
        var dialog = new ContentDialog { Title = heading, Content = input, PrimaryButtonText = "OK", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        return await ShowDialogAsync(dialog) == ContentDialogResult.Primary ? input.Text : null;
    }
    private async Task<int?> ChooseAsync(string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0) return null;
        var list = new ListView { ItemsSource = items, SelectionMode = ListViewSelectionMode.Single, SelectedIndex = 0, MaxHeight = 440, MinWidth = 420, MaxWidth = 760 };
        return await ShowDialogAsync(new ContentDialog { Title = heading, Content = list, PrimaryButtonText = "Select", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary }) == ContentDialogResult.Primary && list.SelectedIndex >= 0 ? list.SelectedIndex : null;
    }
    private async Task<uint?> ColorDialogAsync(string heading, uint initial)
    {
        var panel = new StackPanel { Spacing = 12 };
        var palette = new ColorPalette();
        var input = new TextBox { Header = "Hex color", Text = "#" + (initial & 0xFFFFFF).ToString("X6"), MaxLength = 9, MinWidth = 310 };
        palette.ColorSelected += (_, color) => input.Text = "#" + (color & 0xFFFFFF).ToString("X6");
        panel.Children.Add(palette); panel.Children.Add(input);
        panel.Children.Add(new TextBlock { Text = "Paper suggestions: #FFF9E6, #EDF7EC, #EAF3FD, #FFF0F5, #FFFFFF", FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        if (await ShowDialogAsync(new ContentDialog { Title = heading, Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return null;
        var hex = input.Text.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result)) throw new InvalidDataException("Use a six-digit hexadecimal color, for example #803AB3.");
        return 0xFF000000 | result;
    }
    private async Task<(string Family, float Size)?> FontDialogAsync(TextFormat current)
    {
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        var family = new ComboBox { Header = "Font family", ItemsSource = new[] { "Segoe UI", "Arial", "Calibri", "Cambria", "Georgia", "Times New Roman", "Consolas", "Courier New" }, SelectedItem = current.FontFamily, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (family.SelectedIndex < 0) family.SelectedIndex = 0;
        var size = new TextBox { Header = "Size (6–144)", Text = current.FontSize.ToString(CultureInfo.InvariantCulture), MaxLength = 6 };
        panel.Children.Add(family); panel.Children.Add(size);
        panel.Children.Add(new TextBlock { Text = "Font availability depends on the browser or operating system. Missing fonts use the renderer’s fallback.", FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        if (await ShowDialogAsync(new ContentDialog { Title = "Font", Content = panel, PrimaryButtonText = "Apply", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return null;
        if (!float.TryParse(size.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !float.IsFinite(result) || result is < 6 or > 144) throw new InvalidDataException("Font size must be between 6 and 144.");
        return (family.SelectedItem?.ToString() ?? "Segoe UI", result);
    }
    private async Task TableDialogAsync(string? blockId)
    {
        surface.EndEditing(); if (CurrentPage is null) return;
        var pageId = CurrentPage.Id;
        var block = CurrentPage.Blocks.FirstOrDefault(b => b.Id == blockId && b.Kind == BlockKind.Table);
        var table = new NoteTableEditor(block?.Cells ?? [], theme);
        if (await ShowDialogAsync(new ContentDialog { Title = block is null ? "Insert table" : "Edit table", Content = table, PrimaryButtonText = "Save", CloseButtonText = "Cancel" }) != ContentDialogResult.Primary) return;
        var cells = table.GetCells();
        if (cells.Count is < 1 or > 500 || cells.Any(r => r.Count is < 1 or > 50)) throw new InvalidDataException("Tables support up to 500 rows and 50 columns.");
        if (block is null) surface.InsertBlock(new NoteBlock { Kind = BlockKind.Table, X = 48, Y = NextY(), Width = Math.Clamp(cells.Max(r => r.Count) * 180, 360, 3600), Height = Math.Min(20000, cells.Count * 42), Cells = cells });
        else session.EditPage(pageId, "Edit table", p => { var b = p.Blocks.First(b => b.Id == block.Id); b.Cells = cells; b.Height = Math.Min(20000, cells.Count * 42); });
    }
}
