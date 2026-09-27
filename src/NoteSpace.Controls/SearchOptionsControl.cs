using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Editor;

namespace NoteSpace.Controls;

/// <summary>Reusable search settings shared by the search pane and replace dialog.</summary>
public sealed class SearchOptionsControl : UserControl
{
    private readonly OfficeComboBox scope = new() { Header = "Search in", ItemsSource = new[] { "All notebooks", "This notebook", "This section", "This page" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly OfficeComboBox filter = new() { Header = "Content", ItemsSource = new[] { "All content", "Tags only", "Open to-dos", "Completed to-dos" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly OfficeCheckBox matchCase = new() { Content = "Match case", MinHeight = 28 };
    private readonly OfficeCheckBox wholeWord = new() { Content = "Whole words", MinHeight = 28 };
    public NoteSearchScope Scope { get => (NoteSearchScope)Math.Max(0, scope.SelectedIndex); set => scope.SelectedIndex = (int)value; }
    public NoteSearchFilter Filter { get => (NoteSearchFilter)Math.Max(0, filter.SelectedIndex); set => filter.SelectedIndex = (int)value; }
    public bool MatchCase { get => matchCase.IsChecked == true; set => matchCase.IsChecked = value; }
    public bool WholeWord { get => wholeWord.IsChecked == true; set => wholeWord.IsChecked = value; }
    public event EventHandler? Changed;
    public SearchOptionsControl(bool showFilter = true)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(scope); if (showFilter) panel.Children.Add(filter);
        var flags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        flags.Children.Add(matchCase); flags.Children.Add(wholeWord); panel.Children.Add(flags); Content = panel;
        AutomationProperties.SetAutomationId(scope, "search-scope"); AutomationProperties.SetName(scope, "Search scope");
        AutomationProperties.SetAutomationId(filter, "search-filter"); AutomationProperties.SetName(filter, "Search content filter");
        AutomationProperties.SetAutomationId(matchCase, "search-match-case"); AutomationProperties.SetAutomationId(wholeWord, "search-whole-word");
        scope.SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        filter.SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        matchCase.Checked += OnChanged; matchCase.Unchecked += OnChanged; wholeWord.Checked += OnChanged; wholeWord.Unchecked += OnChanged;
    }
    private void OnChanged(object sender, RoutedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
