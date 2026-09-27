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
