using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;
using Windows.System;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private readonly PageNavigationHistory navigationHistory = new();
    private readonly PaneResizeHandle notebookEdge = new() { DefaultWidth = 204 };
    private readonly PaneResizeHandle pageEdge = new() { DefaultWidth = 206, Value = 206 };
    private readonly PaneResizeHandle searchEdge = new() { DefaultWidth = 310, Minimum = 200, Maximum = 520, Value = 310, Reverse = true, HorizontalAlignment = HorizontalAlignment.Left };
    private OfficeButton? backButton, forwardButton;
    private string? displayedPageId;
    private double? previewNotebookWidth, previewPageWidth, previewSearchWidth;
    private void ToggleNavigationPane()
    {
        if (compactLayout && searchOpen) { searchOpen = false; navigationOpen = true; }
        else navigationOpen = !navigationOpen;
        ApplyLayout(); Report();
    }
    private bool PageExists(string id) => session.FindPage(id) is not null;
    private void ConfigureNavigation()
    {
        foreach (var (edge, column, id, label) in new[] {
            (notebookEdge, 0, "resize-notebooks", "Resize notebook pane"), (pageEdge, 1, "resize-pages", "Resize page pane"), (searchEdge, 3, "resize-search", "Resize search pane") })
        {
            Grid.SetColumn(edge, column); body.Children.Add(edge);
            AutomationProperties.SetAutomationId(edge, id); AutomationProperties.SetName(edge, label);
        }
        notebookEdge.ResizeChanged += (_, change) => ResizePane(0, change);
        pageEdge.ResizeChanged += (_, change) => ResizePane(1, change);
        searchEdge.ResizeChanged += (_, change) => ResizePane(3, change);
        AddShortcut(VirtualKey.Left, VirtualKeyModifiers.Menu, "page-back");
        AddShortcut(VirtualKey.Right, VirtualKeyModifiers.Menu, "page-forward");
        AddShortcut(VirtualKey.PageUp, VirtualKeyModifiers.Control, "previous-page");
        AddShortcut(VirtualKey.PageDown, VirtualKeyModifiers.Control, "next-page");
        AddShortcut(VirtualKey.E, VirtualKeyModifiers.Control, "search");
        AddShortcut(VirtualKey.F1, VirtualKeyModifiers.Control, "collapse-ribbon");
        AddShortcut(VirtualKey.F10, VirtualKeyModifiers.Menu, "focus-selection-toolbar");
    }
    private void ResizePane(int column, PaneResizeChange change)
    {
        if (change.Completed)
        {
            if (column == 0) previewNotebookWidth = null; else if (column == 1) previewPageWidth = null; else previewSearchWidth = null;
            if (!change.Canceled)
            {
                var preferences = session.Document.Settings.Navigation;
                var before = column == 0 ? preferences.NotebookWidth : column == 1 ? preferences.PageWidth : preferences.SearchWidth;
                if (column == 0) preferences.NotebookWidth = change.Width; else if (column == 1) preferences.PageWidth = change.Width; else preferences.SearchWidth = change.Width;
                if (before != change.Width) MarkDirty();
            }
        }
        else if (column == 0) previewNotebookWidth = change.Width; else if (column == 1) previewPageWidth = change.Width; else previewSearchWidth = change.Width;
        ApplyLayout(); Report();
    }
    private void UpdatePaneHandles(bool desktop, NavigationPaneWidths widths)
    {
        foreach (var (edge, width) in new[] { (notebookEdge, widths.Notebooks), (pageEdge, widths.Pages), (searchEdge, widths.Search) })
        {
            edge.Visibility = desktop && width > 0 ? Visibility.Visible : Visibility.Collapsed;
            edge.Value = width; edge.SetTheme(theme);
        }
    }
    private void RememberViewport()
    {
        if (displayedPageId is { } id) navigationHistory.UpdateCurrent(new(id, surface.Viewport));
    }
    private void SynchronizeNavigationPage()
    {
        var id = CurrentPage?.Id;
        if (displayedPageId == id) return;
        RememberViewport(); displayedPageId = id;
        // Organization actions can select another page before the host's explicit Navigate call.
        // Keep that change in browsing history as well, without restoring deleted content.
        if (id is not null)
        {
            surface.SetViewport(new(0, 0, surface.Zoom));
            navigationHistory.Record(new(id, surface.Viewport));
        }
    }
    private void BrowseHistory(int direction)
    {
        surface.EndEditing(); if (surface.HasPendingText) return;
        RememberViewport(); var visit = navigationHistory.Move(direction, PageExists);
        if (visit is null) return;
        Navigate(visit.PageId, restore: visit.Viewport, historyMove: true);
    }
    private void UpdateNavigationButtons()
    {
        ribbon.SetSimplified(session.Document.Settings.Navigation.SimplifiedRibbon);
        surface.SelectionToolbarEnabled = session.Document.Settings.Navigation.ShowSelectionToolbar;
        var back = navigationHistory.Peek(-1, PageExists) is not null;
        var forward = navigationHistory.Peek(1, PageExists) is not null;
        if (backButton is not null) backButton.IsEnabled = back;
        if (forwardButton is not null) forwardButton.IsEnabled = forward;
        ribbon.SetCommandState("page-back", false, back); ribbon.SetCommandState("page-forward", false, forward);
        ribbon.SetCommandState("page-previews", session.Document.Settings.Navigation.ShowPagePreviews);
        ribbon.SetCommandState("page-dates", session.Document.Settings.Navigation.ShowPageDates);
        ribbon.SetCommandState("selection-toolbar", session.Document.Settings.Navigation.ShowSelectionToolbar);
    }
    private void RefreshPagePresentation()
    {
        var options = session.Document.Settings.Navigation;
        // Content changes affect previews, dates and date sorting. Plain manual/title
        // lists do not need to be recreated for body typing; title/outline changes use StructureChanged.
        if (options.ShowPagePreviews || options.ShowPageDates || options.PageSort is PageSortMode.ModifiedNewest or PageSortMode.CreatedNewest)
            pages.Bind(CurrentSection, CurrentPage?.Id, theme, options);
    }
    private void SetPageSort(PageSortMode sort)
    {
        session.Document.Settings.Navigation.PageSort = sort;
        pages.Bind(CurrentSection, CurrentPage?.Id, theme, session.Document.Settings.Navigation);
        pages.FocusSelectedPage(); MarkDirty();
    }
    private bool ExecuteNavigationCommand(string command)
    {
        var options = session.Document.Settings.Navigation;
        switch (command)
        {
            case "simplified-ribbon":
                options.SimplifiedRibbon = !options.SimplifiedRibbon;
                ribbon.SetSimplified(options.SimplifiedRibbon); ApplyLayout(); MarkDirty(); return true;
            case "selection-toolbar":
                options.ShowSelectionToolbar = !options.ShowSelectionToolbar;
                surface.SelectionToolbarEnabled = options.ShowSelectionToolbar; UpdateNavigationButtons(); MarkDirty(); return true;
            case "focus-selection-toolbar": surface.ShowSelectionToolbar(true); return true;
            case "page-back": BrowseHistory(-1); return true;
            case "page-forward": BrowseHistory(1); return true;
            case "previous-page": case "next-page":
                var visible = pages.VisiblePageIds; var index = visible.ToList().IndexOf(CurrentPage?.Id ?? "");
                var next = index + (command == "next-page" ? 1 : -1);
                if (next >= 0 && next < visible.Count) Navigate(visible[next]);
                return true;
            case "sort-manual": SetPageSort(PageSortMode.Manual); return true;
            case "sort-title": SetPageSort(PageSortMode.TitleAscending); return true;
            case "sort-title-descending": SetPageSort(PageSortMode.TitleDescending); return true;
            case "sort-modified": SetPageSort(PageSortMode.ModifiedNewest); return true;
            case "sort-created": SetPageSort(PageSortMode.CreatedNewest); return true;
            case "page-previews": options.ShowPagePreviews = !options.ShowPagePreviews; break;
            case "page-dates": options.ShowPageDates = !options.ShowPageDates; break;
            case "reset-pane-widths":
                previewNotebookWidth = previewPageWidth = previewSearchWidth = null;
                options.NotebookWidth = 204; options.PageWidth = 206; options.SearchWidth = 310;
                ApplyLayout(); MarkDirty(); return true;
            default: return false;
        }
        pages.Bind(CurrentSection, CurrentPage?.Id, theme, options); UpdateNavigationButtons(); MarkDirty(); return true;
    }
}
