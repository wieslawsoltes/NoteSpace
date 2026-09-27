using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NoteSpace.Controls;
using NoteSpace.Core;
using Windows.System;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private void BuildTitleBar()
    {
        titleBar.Background = OfficeTheme.Brush(OfficeTheme.Accent);
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition());
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var white = new OfficeTheme(false, OfficeTheme.Accent, OfficeTheme.Accent, 0xFFFFFFFF, 0xFFE7D8F2, OfficeTheme.Accent, 0xFF9253BF, 0xFF9253BF);
        var quick = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6, 0, 12, 0) };
        quick.Children.Add(new OfficeButton("", "menu", () => { navigationOpen = !navigationOpen; ApplyLayout(); }, "Show or hide notebooks", theme: white) { Width = 32, Height = 32 });
        quick.Children.Add(new NoteIcon { Glyph = "book", InkColor = 0xFFFFFFFF, Width = 25, Height = 25, VerticalAlignment = VerticalAlignment.Center });
        quick.Children.Add(white.Label("NoteSpace", 14, true));
        quick.Children.Add(new OfficeButton("", "undo", () => Invoke("undo"), "Undo (Ctrl+Z)", theme: white) { Width = 29, Height = 30, Margin = new Thickness(8, 0, 0, 0) });
        quick.Children.Add(new OfficeButton("", "redo", () => Invoke("redo"), "Redo (Ctrl+Y)", theme: white) { Width = 29, Height = 30 });
        quick.Children.Add(new OfficeButton("", "save", () => Invoke("save"), "Save (Ctrl+S)", theme: white) { Width = 29, Height = 30 });
        titleBar.Children.Add(quick); Grid.SetColumn(title, 1); titleBar.Children.Add(title);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(10, 0, 10, 0) };
        topSearch.Background = OfficeTheme.Brush(0xFFF7F1FA); topSearch.Foreground = OfficeTheme.Brush(0xFF4A285D); right.Children.Add(topSearch);
        right.Children.Add(new OfficeButton("Export", "download", () => Invoke("export"), "Export a notebook backup", theme: white) { Height = 30 });
        Grid.SetColumn(right, 2); titleBar.Children.Add(right);
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, string command)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) => { Invoke(command); e.Handled = true; }; KeyboardAccelerators.Add(accelerator);
    }
    private void ApplyTheme()
    {
        theme = session.Document.Settings.DarkMode ? OfficeTheme.Dark : OfficeTheme.Light;
        RequestedTheme = theme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        Background = OfficeTheme.Brush(theme.Surface); body.Background = OfficeTheme.Brush(theme.Panel);
        surface.Dark = theme.IsDark;
        ribbon.Configure(RibbonDefinitions.Create(), theme, session.Document.Settings.RibbonCollapsed);
        BindNavigation(); ApplyLayout(); UpdateStatus();
    }
    private void BindNavigation()
    {
        var selected = session.Pages.FirstOrDefault(x => x.Page.Id == CurrentPage?.Id);
        if (selected.Section is not null) sectionId = selected.Section.Id;
        notebooks.Bind(session.Document, sectionId, theme); pages.Bind(CurrentSection, CurrentPage?.Id, theme, recentSort);
        sectionTabs.Children.Clear();
        foreach (var section in (selected.Notebook ?? session.Document.Notebooks.FirstOrDefault())?.Sections ?? [])
        {
            var button = new OfficeButton(section.Title, "", () => SelectSection(section.Id), section.Title, theme: theme) { Height = 30, Padding = new Thickness(18, 4, 18, 4), Selected = section.Id == sectionId, BorderBrush = OfficeTheme.Brush(section.Color), BorderThickness = new Thickness(0, 0, 0, 3) }; sectionTabs.Children.Add(button);
        }
        title.Text = CurrentPage?.Title ?? "NoteSpace"; surface.Refresh();
    }
    private void ApplyLayout()
    {
        var narrow = ActualWidth > 0 && ActualWidth < 900;
        var phone = ActualWidth > 0 && ActualWidth < 680;
        if (compactLayout != phone) { compactLayout = phone; navigationOpen = !phone; }
        body.ColumnDefinitions[0].Width = new GridLength(!focusMode && navigationOpen && (!narrow || !searchOpen) ? narrow ? 150 : 204 : 0);
        body.ColumnDefinitions[1].Width = new GridLength(!focusMode && (!phone || navigationOpen) ? narrow ? 160 : 206 : 0);
        body.ColumnDefinitions[3].Width = new GridLength(searchOpen ? Math.Min(310, Math.Max(200, ActualWidth * 0.3)) : 0);
        notebooks.Visibility = body.ColumnDefinitions[0].Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        pages.Visibility = body.ColumnDefinitions[1].Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        search.Visibility = searchOpen ? Visibility.Visible : Visibility.Collapsed;
        ribbon.Visibility = focusMode ? Visibility.Collapsed : Visibility.Visible;
        sectionStrip.Visibility = !focusMode && session.Document.Settings.HorizontalTabs ? Visibility.Visible : Visibility.Collapsed;
        topSearch.Visibility = ActualWidth > 0 && ActualWidth < 800 ? Visibility.Collapsed : Visibility.Visible;
        title.Visibility = ActualWidth > 0 && ActualWidth < 1100 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UpdateSearch() => search.Bind(session.Search(search.QueryBox.Text), theme);
    private void UpdateStatus()
    {
        var selected = surface.SelectedBlock;
        var tool = surface.Tool == DrawingTool.Select ? "" : " · " + surface.Tool;
        var text = saveStatus + "  ·  " + (CurrentPage is null ? "No page selected" : $"{CurrentPage.Blocks.Count} notes · {CurrentPage.Ink.Count} strokes") + tool;
        if (selected is not null) text += $"  ·  {selected.Kind}";
        status.Update(text, surface.Zoom, theme);
    }
    private void Report()
    {
        if (!ready) return;
        var state = new RuntimeState
        {
            Ready = ready, PageTitle = CurrentPage?.Title, PageId = CurrentPage?.Id,
            PageLevel = CurrentPage?.Level ?? 0, PageCollapsed = CurrentPage?.IsCollapsed ?? false, VisiblePageCount = pages.VisiblePageCount,
            PageCount = session.Pages.Count(), BlockCount = CurrentPage?.Blocks.Count ?? 0,
            InkCount = CurrentPage?.Ink.Count ?? 0, Revision = session.Document.Revision,
            Dirty = changes != savedChanges || surface.HasPendingText, Status = saveStatus,
            SelectedBlockId = surface.SelectedBlockId, Tool = surface.Tool.ToString(), Zoom = surface.Zoom
        };
        // Diagnostics must work with reflection disabled in trimmed WebAssembly builds.
        platform.Report(JsonSerializer.Serialize(state, RuntimeJsonContext.Default.RuntimeState));
    }
}

internal sealed class RuntimeState
{
    public bool Ready { get; set; }
    public string? PageTitle { get; set; }
    public string? PageId { get; set; }
    public int PageLevel { get; set; }
    public bool PageCollapsed { get; set; }
    public int VisiblePageCount { get; set; }
    public int PageCount { get; set; }
    public int BlockCount { get; set; }
    public int InkCount { get; set; }
    public long Revision { get; set; }
    public bool Dirty { get; set; }
    public string Status { get; set; } = "";
    public string? SelectedBlockId { get; set; }
    public string Tool { get; set; } = "Select";
    public float Zoom { get; set; }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RuntimeState))]
internal partial class RuntimeJsonContext : JsonSerializerContext { }
