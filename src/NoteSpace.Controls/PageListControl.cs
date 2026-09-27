using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NoteSpace.Core;
using NoteSpace.Editor;
using Windows.System;

namespace NoteSpace.Controls;

/// <summary>Reusable notebook page outline. Commands are delegated to the host;
/// queries never mutate a workspace. Native list selection retains keyboard navigation.</summary>
public sealed class PageListControl : UserControl
{
    private readonly Grid root = new();
    private readonly StackPanel header = new() { Spacing = 4, Margin = new Thickness(10, 12, 10, 6) };
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, Padding = new Thickness(4, 0, 4, 0) };
    private IReadOnlyList<PageOutlineEntry> outline = [];
    private NoteSection? section;
    private string? selectedId;
    private OfficeTheme theme = OfficeTheme.Light;
    private bool binding, recent, restoreFocus;
    public int VisiblePageCount => list.Items.Count;
    public event EventHandler<string>? PageSelected;
    public event EventHandler<CommandRequest>? CommandInvoked;

    public PageListControl()
    {
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(header); Grid.SetRow(list, 1); root.Children.Add(list); Content = root;
        AutomationProperties.SetName(list, "Pages"); AutomationProperties.SetAutomationId(list, "page-outline");
        list.SelectionChanged += (_, _) => {
            // Uno may deliver a selection notification after a rebind/layout pass.
            // Re-selecting the current page would cancel a newly opened title editor.
            if (!binding && list.SelectedItem is ListViewItem { Tag: string id } && id != selectedId)
            { restoreFocus = true; PageSelected?.Invoke(this, id); }
        };
        list.KeyDown += OnKeyDown;
    }

    public void Bind(NoteSection? value, string? pageId, OfficeTheme palette, bool recent = false)
    {
        binding = true;
        try
        {
            section = value; selectedId = pageId; theme = palette; this.recent = recent;
            outline = PageOutline.Build(section?.Pages ?? []);
            Background = OfficeTheme.Brush(theme.Surface); root.BorderBrush = OfficeTheme.Brush(theme.Border); root.BorderThickness = new Thickness(0, 0, 1, 0);
            ApplyListPalette();
            header.Children.Clear();
            var heading = new Grid(); heading.ColumnDefinitions.Add(new ColumnDefinition()); heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.Children.Add(theme.Label(section?.Title ?? "Pages", 13, true));
            var sort = new OfficeButton(recent ? "Recent" : "Order", "", () => Request("recent"), "Toggle recent page order", theme: theme) { Height = 24, Padding = new Thickness(4, 0, 4, 0), Selected = recent };
            Grid.SetColumn(sort, 1); heading.Children.Add(sort); header.Children.Add(heading);
            var add = new OfficeButton("Add page", "add", () => Request("new-page", section?.Id), "Add page (Ctrl+Alt+N)", theme: theme) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 36, IsEnabled = section is not null };
            AutomationProperties.SetAutomationId(add, "add-page"); header.Children.Add(add);
            list.Items.Clear();
            IEnumerable<PageOutlineEntry> visible = recent ? outline.OrderByDescending(x => x.Page.Modified) : PageOutline.Visible(outline, pageId);
            foreach (var entry in visible) AddRow(entry);
            if (restoreFocus && list.SelectedItem is ListViewItem selected)
            {
                restoreFocus = false;
                DispatcherQueue.TryEnqueue(() => {
                    if (list.SelectedItem == selected) { list.ScrollIntoView(selected); selected.Focus(FocusState.Programmatic); }
                });
            }
        }
        finally { binding = false; }
    }

    private void ApplyListPalette()
    {
        list.Background = OfficeTheme.Brush(theme.Surface); list.Foreground = OfficeTheme.Brush(theme.Text);
        // Cover both WinUI and Uno's compatibility template resource names. Otherwise
        // the platform's blue selected state overrides a row's notebook palette.
        foreach (var key in new[] { "ListViewItemBackgroundSelected", "ListViewItemBackgroundSelectedPointerOver", "ListViewItemBackgroundSelectedPressed", "SystemControlHighlightListAccentLowBrush", "SystemControlHighlightListAccentMediumBrush", "SystemControlHighlightListAccentHighBrush" })
            list.Resources[key] = OfficeTheme.Brush(theme.Selection);
        foreach (var key in new[] { "ListViewItemForegroundSelected", "ListViewItemForegroundSelectedPointerOver", "ListViewItemForegroundSelectedPressed", "ListViewItemForegroundPointerOver", "SystemControlHighlightAltBaseHighBrush" })
            list.Resources[key] = OfficeTheme.Brush(theme.Text);
        foreach (var key in new[] { "ListViewItemBackgroundPointerOver", "ListViewItemBackgroundPressed" }) list.Resources[key] = OfficeTheme.Brush(theme.Hover);
        list.Resources["SystemAccentColorBrush"] = OfficeTheme.Brush(OfficeTheme.Accent);
        list.Resources["ListViewItemSelectionIndicatorBrush"] = OfficeTheme.Brush(OfficeTheme.Accent);
    }

    private void AddRow(PageOutlineEntry entry)
    {
        var page = entry.Page; var selected = page.Id == selectedId;
        var content = new Grid { MinHeight = 32 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); content.ColumnDefinitions.Add(new ColumnDefinition());
        var expandedForSelection = outline.Skip(entry.Index + 1).Take(entry.EndIndex - entry.Index - 1).Any(x => x.Page.Id == selectedId);
        var expanded = !page.IsCollapsed || expandedForSelection;
        if (!recent && entry.HasChildren)
        {
            var disclosure = new OfficeButton(expanded ? "⌄" : "›", "", () => Request(expanded ? "collapse-page" : "expand-page", page.Id), (expanded ? "Collapse subpages of " : "Expand subpages of ") + page.Title, theme: theme) { Width = 22, Height = 26, Padding = new Thickness(0), IsTabStop = false };
            AutomationProperties.SetAutomationId(disclosure, "outline-toggle-" + page.Id); content.Children.Add(disclosure);
        }
        var label = theme.Label((page.IsFavorite ? "★  " : "") + page.Title, 13);
        label.Margin = new Thickness(2, 0, 4, 0); Grid.SetColumn(label, 1); content.Children.Add(label);
        var row = new ListViewItem {
            Tag = page.Id, Padding = new Thickness(2 + (recent ? 0 : 12 * entry.Level), 1, 2, 1), MinHeight = 38,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = content,
            Background = OfficeTheme.Brush(selected ? theme.Selection : 0x00000000),
            BorderBrush = OfficeTheme.Brush(selected ? OfficeTheme.Accent : 0x00000000), BorderThickness = new Thickness(3, 0, 0, 0)
        };
        var actions = new List<(string Id, string Label)> { ("rename-page", "Rename"), ("duplicate-page", "Duplicate page") };
        if (entry.Level < PageOutline.MaximumLevel) actions.Add(("new-subpage", "New subpage"));
        if (entry.HasChildren) actions.Add((expanded ? "collapse-page" : "expand-page", expanded ? "Collapse subpages" : "Expand subpages"));
        actions.AddRange([("move-page", "Move to section…"), ("page-up", "Move up"), ("page-down", "Move down"), ("subpage", "Make subpage"), ("promote-page", "Promote page"), ("favorite", page.IsFavorite ? "Remove favorite" : "Favorite"), ("-", ""), ("delete-page", "Delete page")]);
        row.ContextFlyout = OfficeMenus.Create(id => Request(id, page.Id), actions.ToArray());
        AutomationProperties.SetName(row, page.Title); AutomationProperties.SetAutomationId(row, "page-" + page.Id);
        AutomationProperties.SetHelpText(row, $"Page level {entry.Level + 1}" + (entry.HasChildren ? expanded ? ", expanded" : ", collapsed" : ""));
        list.Items.Add(row); if (selected) list.SelectedItem = row;
    }

    public void FocusSelectedPage()
    {
        if (list.SelectedItem is ListViewItem selected) { list.ScrollIntoView(selected); selected.Focus(FocusState.Keyboard); }
        else list.Focus(FocusState.Keyboard);
    }

    private void Request(string command, string? id = null)
    {
        restoreFocus = command is "collapse-page" or "expand-page" or "page-up" or "page-down" or "subpage" or "promote-page";
        CommandInvoked?.Invoke(this, new(command, id));
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var entry = outline.FirstOrDefault(x => x.Page.Id == selectedId); if (entry is null) return;
        if (e.Key == VirtualKey.F2) { Request("rename-page", selectedId); e.Handled = true; }
        else if (e.Key == VirtualKey.Delete) { Request("delete-page", selectedId); e.Handled = true; }
        else if (!recent && e.Key == VirtualKey.Left)
        {
            if (entry.HasChildren && (!entry.Page.IsCollapsed || outline.Skip(entry.Index + 1).Take(entry.EndIndex - entry.Index - 1).Any(x => x.Page.Id == selectedId))) Request("collapse-page", selectedId);
            else if (entry.ParentIndex >= 0) { restoreFocus = true; PageSelected?.Invoke(this, outline[entry.ParentIndex].Page.Id); }
            e.Handled = true;
        }
        else if (!recent && e.Key == VirtualKey.Right)
        {
            if (entry.HasChildren)
            {
                if (entry.Page.IsCollapsed) Request("expand-page", selectedId);
                else { restoreFocus = true; PageSelected?.Invoke(this, outline[entry.Index + 1].Page.Id); }
            }
            e.Handled = true;
        }
    }
}
