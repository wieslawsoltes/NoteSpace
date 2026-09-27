using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    private readonly ScrollViewer sectionStrip;
    private readonly TextBox topSearch = new() { PlaceholderText = "Search notebooks", Width = 240, Height = 28, MinHeight = 28, FontSize = 12, Padding = new Thickness(12, 3, 12, 3), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock title = new() { FontSize = 12, Foreground = OfficeTheme.Brush(0xFFFFFFFF), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private string? storageToken, sectionId;
    private long changes, savedChanges;
    private bool ready, initializing, saving, saveBlocked, searchOpen, focusMode, navigationOpen = true, recentSort, disposed, compactLayout;
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
        sectionStrip = new ScrollViewer { Content = sectionTabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 34 };
        Grid.SetRow(sectionStrip, 2); root.Children.Add(sectionStrip);
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
        ribbon.TabChanged += (_, tab) => { if (tab != "Draw") surface.Tool = DrawingTool.Select; UpdateStatus(); Report(); };
        notebooks.SectionSelected += (_, id) => SelectSection(id);
        notebooks.CommandInvoked += (_, request) => Invoke(request.CommandId, request.EntityId);
        pages.CanDrop = request => !surface.HasPendingText && session.CanMovePageRelative(request);
        pages.PageMoveRequested += (_, request) => DropPage(request);
        pages.PageSelected += (_, id) => Navigate(id);
        pages.CommandInvoked += (_, request) => Invoke(request.CommandId, request.EntityId);
        surface.CommandRequested += (_, request) => Invoke(request.CommandId, request.EntityId);
        surface.Error += (_, error) => { saveStatus = error; UpdateStatus(); Report(); };
        surface.DraftChanged += (_, _) => { saveStatus = "Saving…"; saveTimer.Stop(); saveTimer.Start(); Report(); UpdateStatus(); };
        surface.SelectionChanged += (_, _) => { UpdateStatus(); Report(); };
        surface.ViewChanged += (_, _) => { if (ready && !initializing) { session.Document.Settings.Zoom = surface.Zoom; MarkDirty(); } };
        status.ZoomRequested += (_, value) => surface.SetZoom(value);
        status.FocusRequested += (_, _) => Invoke("full-page");
        backstage.CommandInvoked += (_, command) => Invoke(command);
        search.QueryBox.TextChanged += (_, _) => UpdateSearch();
        search.ResultSelected += (_, hit) => Navigate(hit.PageId, hit.BlockId);
        topSearch.TextChanged += (_, _) => { searchOpen = topSearch.Text.Length > 0; search.QueryBox.Text = topSearch.Text; ApplyLayout(); UpdateSearch(); };
        saveTimer.Tick += async (_, _) => { saveTimer.Stop(); await SaveAsync(); };
        AddShortcut(VirtualKey.G, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "new-section-group");
        AddShortcut(VirtualKey.F6, VirtualKeyModifiers.None, "focus-pages");
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, "save");
        AddShortcut(VirtualKey.F, VirtualKeyModifiers.Control, "search");
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control, "undo");
        AddShortcut(VirtualKey.Y, VirtualKeyModifiers.Control, "redo");
        AddShortcut(VirtualKey.B, VirtualKeyModifiers.Control, "bold");
        AddShortcut(VirtualKey.I, VirtualKeyModifiers.Control, "italic");
        AddShortcut(VirtualKey.U, VirtualKeyModifiers.Control, "underline");
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "new-page");
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu | VirtualKeyModifiers.Shift, "new-subpage");
        KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { backstage.Visibility = Visibility.Collapsed; searchOpen = false; ApplyLayout(); } };
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += async (_, _) => { if (!ready && !initializing) await InitializeAsync(); };
        ApplyTheme();
    }
    private async Task InitializeAsync()
    {
        if (initializing) return;
        initializing = true;
        StoredWorkspace? loaded = null;
        try { loaded = await platform.Store.LoadAsync(); }
        catch (Exception error)
        {
            ready = true; saveBlocked = true; initializing = false;
            saveStatus = "Storage unavailable — changes are not saved"; ApplyTheme(); Report();
            await MessageAsync("Notebook storage could not be opened", error.Message + "\n\nExisting stored data has not been replaced. You can work in this recovery session and export a .notespace backup.");
            return;
        }
        try
        {
            if (loaded is not null)
            {
                session.Changed -= OnDocumentChanged; session = new EditorSession(loaded.Document);
                surface.Session = session; session.Changed += OnDocumentChanged; storageToken = loaded.Token;
            }
            ready = true; saveStatus = "Saved on this device";
            ApplyTheme(); surface.SetZoom(session.Document.Settings.Zoom);
        }
        finally { initializing = false; }
        if (loaded is null) { MarkDirty(); await SaveAsync(); }
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
        if (surface.HasPendingText) return;
        sectionId = session.Pages.FirstOrDefault(x => x.Page.Id == id).Section?.Id;
        if (compactLayout) navigationOpen = false;
        BindNavigation(); notebooks.RevealSelection(); ApplyLayout(); MarkDirty();
    }
    private void SelectSection(string id)
    {
        sectionId = id; var section = session.FindSection(id); if (section is null) return;
        var page = section.Pages.FirstOrDefault() ?? session.AddPage(id); Navigate(page.Id);
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
