using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Core;

namespace NoteSpace.Controls;

public sealed class NotebookNavigator : UserControl
{
    private readonly StackPanel items = new() { Spacing = 2, Margin = new Thickness(6, 4, 6, 8) };
    private readonly HashSet<string> collapsed = [];
    private readonly HashSet<string> revealed = [];
    private Workspace? document;
    private string? selectedSection;
    private OfficeTheme theme = OfficeTheme.Light;
    public event EventHandler<string>? SectionSelected;
    public event EventHandler<CommandRequest>? CommandInvoked;
    public NotebookNavigator() { Content = new ScrollViewer { Content = items, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; }
    public void Bind(Workspace workspace, string? selectedSectionId, OfficeTheme palette)
    {
        var reveal = document != workspace || selectedSection != selectedSectionId;
        document = workspace; selectedSection = selectedSectionId; theme = palette; Background = OfficeTheme.Brush(theme.Panel);
        if (reveal) RevealSelection(); else Rebuild();
    }
    public void RevealSelection()
    {
        revealed.Clear();
        if (document is not null) foreach (var notebook in document.Notebooks)
            if (notebook.Sections.FirstOrDefault(s => s.Id == selectedSection) is { } section)
            {
                collapsed.Remove(notebook.Id);
                foreach (var id in NotebookGroups.Ancestors(notebook, section.GroupId)) revealed.Add(id);
            }
        Rebuild();
    }
    /// <summary>Reveal a newly created group without changing the selected page or persisted collapse preferences.</summary>
    public void RevealGroup(string notebookId, string? groupId)
    {
        var notebook = document?.Notebooks.FirstOrDefault(n => n.Id == notebookId);
        if (notebook is null) return;
        collapsed.Remove(notebook.Id);
        foreach (var id in NotebookGroups.Ancestors(notebook, groupId)) revealed.Add(id);
        Rebuild();
    }
    private void Request(string command, string? id = null) => CommandInvoked?.Invoke(this, new(command, id));
    private void Rebuild()
    {
        items.Children.Clear(); if (document is null) return;
        var header = new Grid { Height = 46, Margin = new Thickness(6, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(theme.Label("Notebooks", 13, true));
        var add = new OfficeButton("", "add", () => Request("new-notebook"), "New notebook", theme: theme) { Width = 30, Height = 30 };
        Grid.SetColumn(add, 1); header.Children.Add(add); items.Children.Add(header);
        foreach (var n in document.Notebooks)
        {
            var row = new OfficeButton(n.Title, "book", () => { if (!collapsed.Add(n.Id)) collapsed.Remove(n.Id); Rebuild(); }, n.Title + " notebook", theme: theme)
            { Height = 40, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 4, 4, 4) };
            row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            row.Content = NavigationHeading(n.Title, "book", n.Color, !collapsed.Contains(n.Id));
            row.ContextFlyout = OfficeMenus.Create(id => Request(id, n.Id), ("new-section", "New section"), ("new-section-group", "New section group"), ("rename-notebook", "Rename notebook"), ("-", ""), ("delete-notebook", "Delete notebook"));
            items.Children.Add(row);
            if (collapsed.Contains(n.Id)) continue;
            var skipLevel = int.MaxValue;
            foreach (var entry in NotebookGroups.Build(n, includeCollapsed: true))
            {
                if (entry.Level > skipLevel) continue;
                skipLevel = int.MaxValue;
                if (entry.Section is { } section) AddSection(n, section, entry.Level);
                else if (entry.Group is { } group)
                {
                    var expanded = !group.IsCollapsed || revealed.Contains(group.Id);
                    AddGroup(n, group, entry.Level, expanded);
                    if (!expanded) skipLevel = entry.Level;
                }
            }
            var create = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 4, 0, 10) };
            create.Children.Add(new OfficeButton("Section", "add", () => Request("new-section", n.Id), "New section", theme: theme) { Height = 32 });
            create.Children.Add(new OfficeButton("Group", "folder", () => Request("new-section-group", n.Id), "New section group", theme: theme) { Height = 32 });
            items.Children.Add(create);
        }
    }
    private void AddGroup(Notebook notebook, SectionGroup group, int level, bool expanded)
    {
        var row = new OfficeButton((expanded ? "⌄  " : "›  ") + group.Title, "folder", () => {
            if (expanded) revealed.Remove(group.Id); else revealed.Add(group.Id);
            Request(expanded ? "collapse-group" : "expand-group", group.Id); Rebuild();
        }, (expanded ? "Collapse " : "Expand ") + group.Title + " group", theme: theme)
        { Height = 36, Margin = new Thickness(4 + 12 * level, 0, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        row.Content = NavigationHeading(group.Title, "folder", group.Color, expanded);
        row.ContextFlyout = OfficeMenus.Create(id => Request(id, group.Id), ("new-section", "New section in group"), ("new-section-group", "New nested group"), ("rename-group", "Rename group"), ("move-group", "Move group…"), ("group-up", "Move up"), ("group-down", "Move down"), ("-", ""), ("ungroup", "Ungroup (keep all notes)"));
        AutomationProperties.SetAutomationId(row, "group-" + group.Id);
        AutomationProperties.SetHelpText(row, NotebookGroups.Path(notebook, group.Id));
        items.Children.Add(row);
    }
    private Grid NavigationHeading(string text, string icon, uint color, bool expanded)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        content.Children.Add(new NoteIcon { Glyph = icon, InkColor = color, Width = 18, Height = 18, HorizontalAlignment = HorizontalAlignment.Left });
        var label = theme.Label(text, 13, true); Grid.SetColumn(label, 1); content.Children.Add(label);
        var disclosure = new NoteIcon { Glyph = expanded ? "chevron" : "chevron-right", InkColor = theme.Muted, Width = 14, Height = 14 };
        Grid.SetColumn(disclosure, 2); content.Children.Add(disclosure);
        return content;
    }
    private void AddSection(Notebook notebook, NoteSection section, int level)
    {
        var row = new OfficeButton { Theme = theme, Selected = selectedSection == section.Id, Height = 36,
            Margin = new Thickness(4 + 12 * level, 0, 0, 0), Padding = new Thickness(12, 4, 6, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch };
        var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(15) }); content.ColumnDefinitions.Add(new ColumnDefinition());
        content.Children.Add(new Border { Width = 5, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, Background = OfficeTheme.Brush(section.Color), CornerRadius = new CornerRadius(1) });
        var title = theme.Label(section.Title); Grid.SetColumn(title, 1); content.Children.Add(title); row.Content = content;
        row.Click += (_, _) => SectionSelected?.Invoke(this, section.Id);
        row.ContextFlyout = OfficeMenus.Create(id => Request(id, section.Id), ("rename-section", "Rename section"), ("section-color", "Section color"), ("move-section", "Move to notebook or group…"), ("section-up", "Move up"), ("section-down", "Move down"), ("-", ""), ("delete-section", "Delete section"));
        AutomationProperties.SetName(row, section.Title + " section"); AutomationProperties.SetAutomationId(row, "section-" + section.Id);
        ToolTipService.SetToolTip(row, NotebookGroups.Path(notebook, section.GroupId) + " / " + section.Title);
        items.Children.Add(row);
    }
}
