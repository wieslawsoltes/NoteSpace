using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Storage;
using Windows.System;

namespace NoteSpace.App;

public sealed partial class WorkspaceView : UserControl, IDisposable
{
    private readonly PlatformServices platform;
    private EditorSession session = new(SampleWorkspace.Create());
    private readonly Grid root = new();
    private readonly Grid body = new();
    private readonly Grid titleBar = new();
    private readonly RibbonControl ribbon = new();
    private readonly NotebookNavigator notebooks = new();
    private readonly PageListControl pages = new();
    private readonly NoteSurface surface = new();
    private readonly SearchResultsControl search = new();
    private readonly NoteStatusBar status = new();
    private readonly BackstageControl backstage = new();
    private readonly StackPanel sectionTabs = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(12, 0, 8, 0) };
    private readonly TextBox topSearch = new() { PlaceholderText = "Search notebooks", Width = 240, Height = 28, MinHeight = 28, FontSize = 12, Padding = new Thickness(12, 3, 12, 3), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 12, Foreground = OfficeTheme.Brush(0xFFFFFFFF), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private string? storageToken, sectionId;
    private long changes, savedChanges;
    private bool ready, saving, saveBlocked, searchOpen, focusMode, navigationOpen = true, recentSort, disposed;
    private string saveStatus = "Opening notebook…";
    private OfficeTheme theme = OfficeTheme.Light;
    private NotePage? CurrentPage => session.SelectedPage;
    private NoteSection? CurrentSection => session.FindSection(sectionId) ?? session.Pages.FirstOrDefault(x => x.Page.Id == CurrentPage?.Id).Section;
    public WorkspaceView(PlatformServices services)
    {
        platform = services;
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        BuildTitleBar(); root.Children.Add(titleBar);
        Grid.SetRow(ribbon, 1); root.Children.Add(ribbon);
        var tabsScroll = new ScrollViewer { Content = sectionTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 34 };
        Grid.SetRow(tabsScroll, 2); root.Children.Add(tabsScroll);
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(204) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(206) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        body.Children.Add(notebooks); Grid.SetColumn(pages, 1); body.Children.Add(pages);
        Grid.SetColumn(surface, 2); body.Children.Add(surface); Grid.SetColumn(search, 3); body.Children.Add(search);
        Grid.SetRow(body, 3); root.Children.Add(body); Grid.SetRow(status, 4); root.Children.Add(status);
        Grid.SetRowSpan(backstage, 5); root.Children.Add(backstage); Content = root;
        surface.Session = session; session.Changed += OnDocumentChanged;
        ribbon.CommandInvoked += (_, command) => Invoke(command);
        ribbon.TabChanged += (_, tab) => { if (tab != "Draw") surface.Tool = DrawingTool.Select; UpdateStatus(); };
        notebooks.SectionSelected += (_, id) => SelectSection(id);
        notebooks.CommandInvoked += (_, request) => Invoke(request.CommandId, request.EntityId);
        pages.PageSelected += (_, id) => Navigate(id);
        pages.CommandInvoked += (_, request) => Invoke(request.CommandId, request.EntityId);
        surface.CommandRequested += (_, request) => Invoke(request.CommandId, request.EntityId);
        surface.Error += (_, error) => { saveStatus = error; UpdateStatus(); };
        surface.DraftChanged += (_, _) => { saveStatus = "Saving…"; saveTimer.Stop(); saveTimer.Start(); Report(); UpdateStatus(); };
        surface.SelectionChanged += (_, _) => { UpdateStatus(); Report(); };
        surface.ViewChanged += (_, _) => { if (ready) { session.Document.Settings.Zoom = surface.Zoom; MarkDirty(); } };
        status.ZoomRequested += (_, value) => surface.SetZoom(value);
        status.FocusRequested += (_, _) => Invoke("full-page");
        backstage.CommandInvoked += (_, command) => Invoke(command);
        search.QueryBox.TextChanged += (_, _) => UpdateSearch();
        search.ResultSelected += (_, hit) => Navigate(hit.PageId, hit.BlockId);
        topSearch.TextChanged += (_, _) => { searchOpen = topSearch.Text.Length > 0; search.QueryBox.Text = topSearch.Text; ApplyLayout(); UpdateSearch(); };
        saveTimer.Tick += async (_, _) => { saveTimer.Stop(); await SaveAsync(); };
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, "save");
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, "search");
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control, "undo");
        AddShortcut(VirtualKey.Y, VirtualKeyModifiers.Control, "redo");
        AddShortcut(VirtualKey.B, VirtualKeyModifiers.Control, "bold");
        AddShortcut(VirtualKey.I, VirtualKeyModifiers.Control, "italic");
        AddShortcut(VirtualKey.U, VirtualKeyModifiers.Control, "underline");
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "new-page");
        KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { backstage.Visibility = Visibility.Collapsed; searchOpen = false; ApplyLayout(); } };
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += async (_, _) => { if (!ready) await InitializeAsync(); };
        ApplyTheme();
    }
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
    private async Task InitializeAsync()
    {
        try
        {
            var loaded = await platform.Store.LoadAsync();
            if (loaded is not null)
            {
                session.Changed -= OnDocumentChanged; session = new EditorSession(loaded.Document);
                surface.Session = session; session.Changed += OnDocumentChanged; storageToken = loaded.Token;
            }
            ready = true; saveStatus = "Saved on this device"; ApplyTheme();
            surface.SetZoom(session.Document.Settings.Zoom);
            if (loaded is null) { MarkDirty(); await SaveAsync(); }
        }
        catch (Exception error)
        {
            ready = true; saveBlocked = true;
            saveStatus = "Storage unavailable — changes are not saved"; ApplyTheme();
            await MessageAsync("Notebook storage could not be opened", error.Message + "\n\nExisting stored data has not been replaced. You can work in this recovery session and export a .notespace backup.");
        }
        UpdateStatus(); Report();
    }
    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        MarkDirty();
        if (change.StructureChanged) BindNavigation();
        UpdateStatus(); UpdateSearch(); Report();
    }
    private void MarkDirty()
    {
        changes++;
        if (ready && !saveBlocked) { saveStatus = "Saving…"; saveTimer.Stop(); saveTimer.Start(); }
        UpdateStatus(); Report();
    }
    private async Task SaveAsync()
    {
        surface.FlushPendingText();
        if (!ready || saving || saveBlocked || disposed || surface.HasPendingText) return;
        saving = true;
        try
        {
            var version = changes; var snapshot = DocumentJson.Clone(session.Document);
            storageToken = await platform.Store.SaveAsync(snapshot, storageToken);
            savedChanges = version; saveStatus = changes == version ? "Saved on this device" : "Saving…";
            if (changes != version) saveTimer.Start();
        }
        catch (Exception error)
        {
            saveStatus = error is StorageConflictException ? "Conflict — export your changes, then reload" : "Save failed — export a backup";
            if (error is StorageConflictException) saveBlocked = true;
            await MessageAsync("Your changes have not been saved", error.Message + "\n\nUse File → Export notebook to preserve this session.");
        }
        finally { saving = false; UpdateStatus(); Report(); }
    }
    private void Navigate(string id, string? blockId = null)
    {
        surface.NavigateToPage(id, blockId);
        sectionId = session.Pages.FirstOrDefault(x => x.Page.Id == id).Section?.Id;
        BindNavigation(); MarkDirty();
    }
    private void SelectSection(string id)
    {
        sectionId = id; var section = session.FindSection(id); if (section is null) return;
        var page = section.Pages.FirstOrDefault() ?? session.AddPage(id); Navigate(page.Id);
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
        body.ColumnDefinitions[0].Width = new GridLength(!focusMode && navigationOpen && (!narrow || !searchOpen) ? narrow ? 150 : 204 : 0);
        body.ColumnDefinitions[1].Width = new GridLength(!focusMode && (ActualWidth >= 680 || navigationOpen) ? narrow ? 160 : 206 : 0);
        body.ColumnDefinitions[3].Width = new GridLength(searchOpen ? Math.Min(310, Math.Max(200, ActualWidth * 0.3)) : 0);
        notebooks.Visibility = body.ColumnDefinitions[0].Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        pages.Visibility = body.ColumnDefinitions[1].Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        search.Visibility = searchOpen ? Visibility.Visible : Visibility.Collapsed;
        ribbon.Visibility = focusMode ? Visibility.Collapsed : Visibility.Visible;
        var parent = sectionTabs.Parent as ScrollViewer;
        if (parent is not null) parent.Visibility = !focusMode && session.Document.Settings.HorizontalTabs ? Visibility.Visible : Visibility.Collapsed;
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
        platform.Report(JsonSerializer.Serialize(new { ready, pageTitle = CurrentPage?.Title, pageId = CurrentPage?.Id, pageCount = session.Pages.Count(), blockCount = CurrentPage?.Blocks.Count ?? 0, inkCount = CurrentPage?.Ink.Count ?? 0, revision = session.Document.Revision, dirty = changes != savedChanges || surface.HasPendingText, status = saveStatus, selectedBlockId = surface.SelectedBlockId, tool = surface.Tool.ToString(), zoom = surface.Zoom }));
    }
    private async void Invoke(string command, string? entityId = null)
    {
        if (!ready && command != "about") return;
        try { await ExecuteCommandAsync(command, entityId); }
        catch (Exception error) { await MessageAsync("Could not complete the action", error.Message); }
        UpdateStatus(); Report();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; saveTimer.Stop(); session.Changed -= OnDocumentChanged; surface.Dispose();
    }
}
