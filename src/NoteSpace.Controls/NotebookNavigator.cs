using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Core;

namespace NoteSpace.Controls;

public sealed class NotebookNavigator : UserControl
{
    private readonly StackPanel items = new() { Spacing = 2, Margin = new Thickness(6, 4, 6, 8) };
    private readonly HashSet<string> collapsed = [];
    private Workspace? document;
    private string? selectedSection;
    private OfficeTheme theme = OfficeTheme.Light;
    public event EventHandler<string>? SectionSelected;
    public event EventHandler<CommandRequest>? CommandInvoked;
    public NotebookNavigator() { Content = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; }
    public void Bind(Workspace workspace, string? selectedSectionId, OfficeTheme palette)
    {
        document = workspace; selectedSection = selectedSectionId; theme = palette; Background = OfficeTheme.Brush(theme.Panel); Rebuild();
    }
    private void Rebuild()
    {
        items.Children.Clear(); if (document is null) return;
        var header = new Grid { Height = 46, Margin = new Thickness(6, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(theme.Label("Notebooks", 13, true));
        var add = new OfficeButton("", "add", () => CommandInvoked?.Invoke(this, new("new-notebook")), "New notebook", theme: theme) { Width = 30, Height = 30 }; Grid.SetColumn(add, 1); header.Children.Add(add); items.Children.Add(header);
        foreach (var n in document.Notebooks)
        {
            var row = new OfficeButton { Theme = theme, Height = 40, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 4, 4, 4) };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            content.Children.Add(new NoteIcon { Glyph = "book", InkColor = n.Color, Width = 20, Height = 20 }); content.Children.Add(theme.Label(n.Title, 13, true)); content.Children.Add(theme.Label(collapsed.Contains(n.Id) ? "›" : "⌄", 12)); row.Content = content;
            row.Click += (_, _) => { if (!collapsed.Add(n.Id)) collapsed.Remove(n.Id); Rebuild(); };
            row.ContextFlyout = OfficeMenus.Create(id => CommandInvoked?.Invoke(this, new(id, n.Id)), ("new-section", "New section"), ("rename-notebook", "Rename notebook"), ("-", ""), ("delete-notebook", "Delete notebook"));
            AutomationProperties.SetName(row, n.Title + " notebook"); items.Children.Add(row);
            if (collapsed.Contains(n.Id)) continue;
            foreach (var s in n.Sections)
            {
                var section = new OfficeButton { Theme = theme, Selected = selectedSection == s.Id, Height = 36, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(12, 4, 6, 4), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
                var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                stack.Children.Add(new Border { Width = 5, Height = 22, Background = OfficeTheme.Brush(s.Color), CornerRadius = new CornerRadius(1) }); stack.Children.Add(theme.Label(s.Title)); section.Content = stack;
                section.Click += (_, _) => SectionSelected?.Invoke(this, s.Id);
                section.ContextFlyout = OfficeMenus.Create(id => CommandInvoked?.Invoke(this, new(id, s.Id)), ("rename-section", "Rename section"), ("section-color", "Section color"), ("section-up", "Move up"), ("section-down", "Move down"), ("-", ""), ("delete-section", "Delete section"));
                AutomationProperties.SetName(section, s.Title + " section"); AutomationProperties.SetAutomationId(section, "section-" + s.Id); items.Children.Add(section);
            }
            items.Children.Add(new OfficeButton("New section", "add", () => CommandInvoked?.Invoke(this, new("new-section", n.Id)), "New section", theme: theme) { Height = 32, Margin = new Thickness(14, 4, 0, 10), HorizontalAlignment = HorizontalAlignment.Left });
        }
    }
}

public sealed class PageListControl : UserControl
{
    private readonly Grid root = new();
    private readonly StackPanel header = new() { Spacing = 4, Margin = new Thickness(10, 12, 10, 6) };
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, Padding = new Thickness(4, 0, 4, 0) };
    private NoteSection? section;
    private string? selectedId;
    private OfficeTheme theme = OfficeTheme.Light;
    private bool binding;
    public event EventHandler<string>? PageSelected;
    public event EventHandler<CommandRequest>? CommandInvoked;
    public PageListControl()
    {
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(header); Grid.SetRow(list, 1); root.Children.Add(list); Content = root;
        list.SelectionChanged += (_, _) => { if (!binding && list.SelectedItem is ListViewItem { Tag: string id }) PageSelected?.Invoke(this, id); };
    }
    public void Bind(NoteSection? value, string? pageId, OfficeTheme palette, bool recent = false)
    {
        binding = true;
        try
        {
            section = value; selectedId = pageId; theme = palette;
            Background = OfficeTheme.Brush(theme.Surface); root.BorderBrush = OfficeTheme.Brush(theme.Border); root.BorderThickness = new Thickness(0, 0, 1, 0);
            header.Children.Clear(); header.Children.Add(theme.Label(section?.Title ?? "Pages", 13, true));
            var add = new OfficeButton("Add page", "add", () => CommandInvoked?.Invoke(this, new("new-page", section?.Id)), "Add page (Ctrl+Alt+N)", theme: theme) { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 36, IsEnabled = section is not null };
            AutomationProperties.SetAutomationId(add, "add-page"); header.Children.Add(add);
            list.Items.Clear(); list.Background = OfficeTheme.Brush(theme.Surface); list.Foreground = OfficeTheme.Brush(theme.Text);
            if (section is null) return;
            IEnumerable<NotePage> pages = recent ? section.Pages.OrderByDescending(p => p.Modified) : section.Pages;
            foreach (var page in pages)
            {
                var row = new ListViewItem { Tag = page.Id, Padding = new Thickness(10 + 14 * page.Level, 5, 8, 5), MinHeight = 38, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = OfficeTheme.Brush(page.Id == selectedId ? theme.Selection : 0x00000000), Content = theme.Label((page.IsFavorite ? "★  " : "") + page.Title, 13) };
                row.ContextFlyout = OfficeMenus.Create(id => CommandInvoked?.Invoke(this, new(id, page.Id)), ("rename-page", "Rename"), ("duplicate-page", "Duplicate page"), ("move-page", "Move to section…"), ("page-up", "Move up"), ("page-down", "Move down"), ("subpage", "Make subpage"), ("promote-page", "Promote page"), ("favorite", page.IsFavorite ? "Remove favorite" : "Favorite"), ("-", ""), ("delete-page", "Delete page"));
                AutomationProperties.SetName(row, page.Title); AutomationProperties.SetAutomationId(row, "page-" + page.Id); list.Items.Add(row);
                if (page.Id == selectedId) list.SelectedItem = row;
            }
        }
        finally { binding = false; }
    }
}
