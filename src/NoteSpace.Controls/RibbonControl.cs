using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace NoteSpace.Controls;

public sealed record RibbonCommand(string Id, string Label, string Icon, string? Hint = null, bool Compact = false);
public sealed record RibbonGroup(string Title, IReadOnlyList<RibbonCommand> Commands);
public sealed record RibbonTab(string Title, IReadOnlyList<RibbonGroup> Groups);

public sealed class RibbonGroupControl : UserControl
{
    public RibbonGroupControl(RibbonGroup group, OfficeTheme theme, Action<string> invoke, Action<string, OfficeButton>? register = null)
    {
        var root = new Grid { Margin = new Thickness(4, 2, 4, 2), Padding = new Thickness(2, 0, 10, 0), BorderBrush = OfficeTheme.Brush(theme.Border), BorderThickness = new Thickness(0, 0, 1, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(78) }); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
        var body = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        StackPanel? compact = null; var count = 0;
        foreach (var command in group.Commands)
        {
            var button = new OfficeButton(command.Label, command.Icon, () => invoke(command.Id), command.Hint ?? command.Label, !command.Compact, theme);
            AutomationProperties.SetAutomationId(button, "command-" + command.Id);
            register?.Invoke(command.Id, button);
            if (command.Compact)
            {
                if (compact is null || count == 3) { compact = new StackPanel { Spacing = 0 }; body.Children.Add(compact); count = 0; }
                button.Height = 26; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Left; compact.Children.Add(button); count++;
            }
            else { compact = null; count = 0; button.MinWidth = 50; button.Height = 74; body.Children.Add(button); }
        }
        root.Children.Add(body);
        var caption = theme.Label(group.Title, 10, false, theme.Muted); caption.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetRow(caption, 1); root.Children.Add(caption);
        Content = root;
    }
}

public sealed class RibbonControl : UserControl
{
    private readonly Grid root = new();
    private readonly StackPanel tabs = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 0, 8, 0) };
    private readonly StackPanel groups = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 0, 6, 0) };
    private readonly ScrollViewer groupScroll;
    private IReadOnlyList<RibbonTab> definitions = [];
    public event EventHandler<string>? CommandInvoked;
    public event EventHandler<string>? TabChanged;
    public OfficeTheme Theme { get; private set; } = OfficeTheme.Light;
    public string ActiveTab { get; private set; } = "Home";
    public bool Collapsed { get; private set; }
    private readonly Dictionary<string, OfficeButton> commandButtons = new();
    private readonly Dictionary<string, (bool Selected, bool Enabled)> commandStates = new();
    public void SetCommandState(string id, bool selected, bool enabled = true)
    {
        commandStates[id] = (selected, enabled);
        if (commandButtons.TryGetValue(id, out var button)) { button.Selected = selected; button.IsEnabled = enabled; }
    }
    private void RegisterCommand(string id, OfficeButton button)
    {
        commandButtons[id] = button;
        if (commandStates.TryGetValue(id, out var state)) { button.Selected = state.Selected; button.IsEnabled = state.Enabled; }
    }
    public RibbonControl()
    {
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tabScroll = new ScrollViewer { Content = tabs, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        root.Children.Add(tabScroll);
        groupScroll = new ScrollViewer { Content = groups, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, Height = 104 };
        Grid.SetRow(groupScroll, 1); root.Children.Add(groupScroll); Content = root;
    }
    public void Configure(IReadOnlyList<RibbonTab> tabsDefinition, OfficeTheme theme, bool collapsed)
    {
        definitions = tabsDefinition; Theme = theme; Collapsed = collapsed; Background = OfficeTheme.Brush(theme.Surface);
        root.BorderBrush = OfficeTheme.Brush(theme.Border); root.BorderThickness = new Thickness(0, 0, 0, 1); SelectTab(ActiveTab);
    }
    public void SelectTab(string title)
    {
        ActiveTab = definitions.Any(t => t.Title == title) ? title : definitions.FirstOrDefault()?.Title ?? "Home";
        tabs.Children.Clear();
        var file = new OfficeButton("File", "", () => CommandInvoked?.Invoke(this, "file"), "File", theme: Theme) { Width = 52, Height = 34, Foreground = OfficeTheme.Brush(OfficeTheme.Accent) }; tabs.Children.Add(file);
        foreach (var tab in definitions)
        {
            var button = new OfficeButton(tab.Title, "", () => { SelectTab(tab.Title); TabChanged?.Invoke(this, tab.Title); }, tab.Title, theme: Theme) { Height = 36, Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(0) };
            if (tab.Title == ActiveTab) { button.BorderBrush = OfficeTheme.Brush(OfficeTheme.Accent); button.BorderThickness = new Thickness(0, 0, 0, 3); }
            AutomationProperties.SetAutomationId(button, "tab-" + tab.Title.ToLowerInvariant()); tabs.Children.Add(button);
        }
        tabs.Children.Add(new OfficeButton(Collapsed ? "⌄" : "⌃", "", () => CommandInvoked?.Invoke(this, "collapse-ribbon"), "Collapse or expand the ribbon", theme: Theme) { Width = 32 });
        commandButtons.Clear(); groups.Children.Clear();
        var active = definitions.FirstOrDefault(t => t.Title == ActiveTab);
        if (active is not null) foreach (var group in active.Groups) groups.Children.Add(new RibbonGroupControl(group, Theme, id => CommandInvoked?.Invoke(this, id), RegisterCommand));
        groupScroll.Visibility = Collapsed ? Visibility.Collapsed : Visibility.Visible;
    }
}
