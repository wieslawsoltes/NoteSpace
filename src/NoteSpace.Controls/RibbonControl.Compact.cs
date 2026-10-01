using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Editor;

namespace NoteSpace.Controls;

public sealed partial class RibbonControl
{
    private readonly Grid compactHost = new() { Height = 46, Margin = new Thickness(8, 0, 8, 0) };
    private readonly StackPanel compactStrip = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private OfficeButton? moreCommands;
    private readonly List<(RibbonCommand Command, string Group, OfficeButton Button)> compactEntries = [];
    private readonly Dictionary<string, MenuFlyoutItem> overflowItems = new(StringComparer.Ordinal);
    private double[] compactWidths = [];
    private int visibleCommands = -1;
    public bool Simplified { get; private set; }
    public int VisibleCommandCount => Simplified ? Math.Max(0, visibleCommands) : commandButtons.Count;
    public int OverflowCommandCount => Simplified ? compactEntries.Count - Math.Max(0, visibleCommands) : 0;
    public long CompactRebuilds { get; private set; }
    public void SetSimplified(bool value)
    {
        if (Simplified == value) return;
        Simplified = value; SelectTab(ActiveTab);
    }
    private void InitializeCompactRibbon()
    {
        compactHost.ColumnDefinitions.Add(new ColumnDefinition());
        compactHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        compactHost.Children.Add(compactStrip);
        Grid.SetRow(compactHost, 1); root.Children.Add(compactHost);
        compactHost.Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(compactHost, "simplified-ribbon");
        SizeChanged += (_, _) => UpdateCompactOverflow();
    }
    private void AddRibbonModeButton()
    {
        var toggle = new OfficeButton(Simplified ? "Classic ribbon" : "Simplified ribbon", "", () => CommandInvoked?.Invoke(this, "simplified-ribbon"),
            Simplified ? "Switch to classic ribbon" : "Switch to simplified ribbon", theme: Theme) { Height = 30, Padding = new Thickness(8, 3, 8, 3) };
        AutomationProperties.SetAutomationId(toggle, "ribbon-mode-toggle"); tabs.Children.Add(toggle);
    }
    private void BuildCompactRibbon(RibbonTab? active)
    {
        compactStrip.Children.Clear(); compactEntries.Clear(); overflowItems.Clear(); compactWidths = []; visibleCommands = -1;
        if (moreCommands is not null) { compactHost.Children.Remove(moreCommands); moreCommands = null; }
        compactHost.Visibility = Simplified && !Collapsed ? Visibility.Visible : Visibility.Collapsed;
        if (!Simplified || active is null) return;
        foreach (var group in active.Groups) foreach (var command in group.Commands)
        {
            var isFont = command.Id == "font";
            var button = new OfficeButton(isFont ? "Font / size" : "", command.Icon, () => CommandInvoked?.Invoke(this, command.Id),
                command.Hint ?? command.Label, theme: Theme) { Width = isFont ? 116 : 34, Height = 34, Padding = new Thickness(4) };
            AutomationProperties.SetAutomationId(button, "command-" + command.Id);
            RegisterCommand(command.Id, button);
            compactEntries.Add((command, group.Title, button)); compactStrip.Children.Add(button);
        }
        compactWidths = compactEntries.Select(e => e.Button.Width).ToArray();
        moreCommands = new OfficeButton("…", "", () => { }, "More ribbon commands", theme: Theme) { Width = 40, Height = 34, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(moreCommands, "ribbon-overflow");
        Grid.SetColumn(moreCommands, 1); compactHost.Children.Add(moreCommands);
        UpdateCompactOverflow();
    }
    private void UpdateCompactOverflow()
    {
        if (!Simplified || moreCommands is null) return;
        var count = EditorChromeLayout.VisibleCommands(compactWidths, Math.Max(0, ActualWidth - 16));
        if (count == visibleCommands) return;
        visibleCommands = count; CompactRebuilds++;
        for (var i = 0; i < compactEntries.Count; i++) compactEntries[i].Button.Visibility = i < count ? Visibility.Visible : Visibility.Collapsed;
        moreCommands.Visibility = count < compactEntries.Count ? Visibility.Visible : Visibility.Collapsed;
        overflowItems.Clear(); var menu = new MenuFlyout(); string? previousGroup = null;
        foreach (var entry in compactEntries.Skip(count))
        {
            if (previousGroup is not null && previousGroup != entry.Group) menu.Items.Add(new MenuFlyoutSeparator());
            previousGroup = entry.Group;
            var item = new MenuFlyoutItem { Text = entry.Command.Label };
            AutomationProperties.SetAutomationId(item, "overflow-" + entry.Command.Id);
            item.Click += (_, _) => CommandInvoked?.Invoke(this, entry.Command.Id);
            overflowItems[entry.Command.Id] = item;
            if (commandStates.TryGetValue(entry.Command.Id, out var state)) UpdateOverflowState(entry.Command.Id, state.Selected, state.Enabled);
            menu.Items.Add(item);
        }
        moreCommands.Flyout = menu;
    }
    private void UpdateOverflowState(string id, bool selected, bool enabled)
    {
        if (!overflowItems.TryGetValue(id, out var item)) return;
        item.IsEnabled = enabled; item.Icon = selected ? new SymbolIcon(Symbol.Accept) : null;
    }
}
