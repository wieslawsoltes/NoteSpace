using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;
using Microsoft.UI.Xaml.Automation;
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
        quick.Children.Add(new OfficeButton("", "menu", () => { if (dialogGate.CurrentCount == 0) return; ToggleNavigationPane(); }, "Show or hide notebooks", theme: white) { Width = 32, Height = 32 });
        quick.Children.Add(new NoteIcon { Glyph = "book", InkColor = 0xFFFFFFFF, Width = 25, Height = 25, VerticalAlignment = VerticalAlignment.Center });
        quick.Children.Add(white.Label("NoteSpace", 14, true));
        quick.Children.Add(new OfficeButton("", "undo", () => Invoke("undo"), "Undo (Ctrl+Z)", theme: white) { Width = 29, Height = 30, Margin = new Thickness(8, 0, 0, 0) });
        quick.Children.Add(new OfficeButton("", "redo", () => Invoke("redo"), "Redo (Ctrl+Y)", theme: white) { Width = 29, Height = 30 });
        quick.Children.Add(new OfficeButton("", "save", () => Invoke("save"), "Save (Ctrl+S)", theme: white) { Width = 29, Height = 30 });
        backButton = new OfficeButton("", "back", () => Invoke("page-back"), "Back (Alt+Left)", theme: white) { Width = 29, Height = 30 };
        forwardButton = new OfficeButton("", "forward", () => Invoke("page-forward"), "Forward (Alt+Right)", theme: white) { Width = 29, Height = 30 };
        AutomationProperties.SetAutomationId(backButton, "page-back"); AutomationProperties.SetAutomationId(forwardButton, "page-forward");
        quick.Children.Add(backButton); quick.Children.Add(forwardButton);
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
    private void BindNavigation(bool selectionOnly = false)
    {
        SynchronizeNavigationPage();
        var selected = session.Pages.FirstOrDefault(x => x.Page.Id == CurrentPage?.Id);
        if (selected.Section is not null) sectionId = selected.Section.Id;
        if (selectionOnly) pages.SelectPage(CurrentPage?.Id);
        else { notebooks.Bind(session.Document, sectionId, theme); pages.Bind(CurrentSection, CurrentPage?.Id, theme, session.Document.Settings.Navigation); }
        if (!selectionOnly)
        {
            sectionTabs.Children.Clear();
            foreach (var section in (selected.Notebook ?? session.Document.Notebooks.FirstOrDefault())?.Sections ?? [])
            {
                var button = new OfficeButton(section.Title, "", () => SelectSection(section.Id), NotebookGroups.Path(selected.Notebook ?? session.Document.Notebooks.First(n => n.Sections.Contains(section)), section.GroupId) + " / " + section.Title, theme: theme) { Height = 30, Padding = new Thickness(18, 4, 18, 4), Selected = section.Id == sectionId, BorderBrush = OfficeTheme.Brush(section.Color), BorderThickness = new Thickness(0, 0, 0, 3) }; sectionTabs.Children.Add(button);
            }
        }
        title.Text = CurrentPage?.Title ?? "NoteSpace"; surface.Refresh(); UpdateNavigationButtons();
    }
    private void ApplyLayout()
    {
        var narrow = ActualWidth > 0 && ActualWidth < 900;
        var phone = ActualWidth > 0 && ActualWidth < 680;
        if (compactLayout != phone) { compactLayout = phone; navigationOpen = !phone; }
        var options = session.Document.Settings.Navigation.Copy();
        options.NotebookWidth = previewNotebookWidth ?? options.NotebookWidth;
        options.PageWidth = previewPageWidth ?? options.PageWidth;
        options.SearchWidth = previewSearchWidth ?? options.SearchWidth;
        var available = ActualWidth > 0 ? ActualWidth : 1600;
        var widths = NavigationPaneLayout.Fit(available, options, !focusMode && navigationOpen && (!narrow || !searchOpen), !focusMode && (!phone || navigationOpen), searchOpen);
        var overlay = phone && (searchOpen || !focusMode && navigationOpen);
        if (overlay) widths = searchOpen ? new(0, 0, available) : new(available * 0.45, available * 0.55, 0);
        body.ColumnDefinitions[0].Width = new GridLength(widths.Notebooks);
        body.ColumnDefinitions[1].Width = new GridLength(widths.Pages);
        body.ColumnDefinitions[2].Width = overlay ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        body.ColumnDefinitions[3].Width = new GridLength(widths.Search);
        surface.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;
        UpdatePaneHandles(!narrow, widths);
        if (backButton is not null) backButton.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
        if (forwardButton is not null) forwardButton.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
        notebooks.Visibility = body.ColumnDefinitions[0].Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        pages.Visibility = body.ColumnDefinitions[1].Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        search.Visibility = searchOpen ? Visibility.Visible : Visibility.Collapsed;
        ribbon.Visibility = focusMode ? Visibility.Collapsed : Visibility.Visible;
        sectionStrip.Visibility = !focusMode && session.Document.Settings.HorizontalTabs ? Visibility.Visible : Visibility.Collapsed;
        topSearch.Visibility = ActualWidth > 0 && ActualWidth < 800 ? Visibility.Collapsed : Visibility.Visible;
        title.Visibility = ActualWidth > 0 && ActualWidth < 1100 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UpdateSearch() { if (searchOpen) search.Bind(session.Search(Query(search.QueryBox.Text, search.Options)), theme); }
    private void UpdateStatus()
    {
        var selected = surface.SelectedBlock;
        UpdateTextRibbon();
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
            NotebookPaneWidth = body.ColumnDefinitions[0].Width.Value, PagePaneWidth = body.ColumnDefinitions[1].Width.Value,
            SearchPaneWidth = body.ColumnDefinitions[3].Width.Value, SearchOpen = searchOpen,
            PageSort = session.Document.Settings.Navigation.PageSort.ToString(), PagePreviews = session.Document.Settings.Navigation.ShowPagePreviews,
            PageDates = session.Document.Settings.Navigation.ShowPageDates, PageRowsBuilt = pages.RowsBuilt,
            CanGoBack = navigationHistory.Peek(-1, PageExists) is not null, CanGoForward = navigationHistory.Peek(1, PageExists) is not null,
            ViewOffsetX = surface.Viewport.OffsetX, ViewOffsetY = surface.Viewport.OffsetY,
            RichTextEditing = surface.IsRichTextEditing, TextSelectionStart = surface.TextSelectionStart, TextSelectionLength = surface.TextSelectionLength,
            TextBold = surface.CurrentTextFormat.Bold, TextItalic = surface.CurrentTextFormat.Italic, TextFontSize = surface.CurrentTextFormat.FontSize,
            EditingTableCell = surface.IsEditingTableCell, TableRow = surface.SelectedTableCell?.Row, TableColumn = surface.SelectedTableCell?.Column,
            HistoryCharacters = session.RetainedHistoryCharacters, SpatialBuilds = surface.Renderer.Statistics.SpatialBuilds,
            LayoutBuilds = surface.Renderer.Statistics.LayoutBuilds, InkPictureBuilds = surface.Renderer.Statistics.InkPictureBuilds,
            NotebookPaneVisible = notebooks.Visibility == Visibility.Visible,
            Ready = ready, PageTitle = CurrentPage?.Title, PageId = CurrentPage?.Id,
            SectionGroupCount = session.Document.Notebooks.Sum(n => n.SectionGroups.Count),
            SectionId = CurrentSection?.Id,
            SectionGroupId = CurrentSection?.GroupId,
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
    public double NotebookPaneWidth { get; set; }
    public double PagePaneWidth { get; set; }
    public double SearchPaneWidth { get; set; }
    public bool SearchOpen { get; set; }
    public string PageSort { get; set; } = "Manual";
    public bool PagePreviews { get; set; }
    public bool PageDates { get; set; }
    public long PageRowsBuilt { get; set; }
    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }
    public float ViewOffsetX { get; set; }
    public float ViewOffsetY { get; set; }
    public bool RichTextEditing { get; set; }
    public int TextSelectionStart { get; set; }
    public int TextSelectionLength { get; set; }
    public bool TextBold { get; set; }
    public bool TextItalic { get; set; }
    public float TextFontSize { get; set; }
    public bool EditingTableCell { get; set; }
    public int? TableRow { get; set; }
    public int? TableColumn { get; set; }
    public long HistoryCharacters { get; set; }
    public long SpatialBuilds { get; set; }
    public long LayoutBuilds { get; set; }
    public long InkPictureBuilds { get; set; }
    public bool NotebookPaneVisible { get; set; }
    public bool Ready { get; set; }
    public string? PageTitle { get; set; }
    public string? PageId { get; set; }
    public int SectionGroupCount { get; set; }
    public string? SectionId { get; set; }
    public string? SectionGroupId { get; set; }
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
