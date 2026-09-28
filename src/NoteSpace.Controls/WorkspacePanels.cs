using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.Controls;

public sealed class SearchResultsControl : UserControl
{
    private readonly Grid root = new();
    private readonly StackPanel results = new() { Spacing = 6, Margin = new Thickness(12) };
    public TextBox QueryBox { get; } = new() { PlaceholderText = "Search all notebooks", Margin = new Thickness(12), FontSize = 14 };
    public SearchOptionsControl Options { get; } = new();
    public event EventHandler<SearchHit>? ResultSelected;
    public SearchResultsControl()
    {
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition()); var header = new StackPanel(); header.Children.Add(QueryBox);
        Options.Margin = new Thickness(12, 0, 12, 8); header.Children.Add(Options); root.Children.Add(header);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(QueryBox, "notebook-search");
        var scroll = new ScrollViewer { Content = results }; Grid.SetRow(scroll, 1); root.Children.Add(scroll); Content = root;
    }
    public void Bind(IEnumerable<SearchHit> hits, OfficeTheme theme)
    {
        Background = OfficeTheme.Brush(theme.Panel); results.Children.Clear();
        var all = hits.Take(201).ToList(); results.Children.Add(theme.Label(all.Count > 200 ? "200+ results" : $"{all.Count} results", 12, false, theme.Muted));
        foreach (var hit in all.Take(200))
        {
            var content = new StackPanel { Spacing = 5 }; content.Children.Add(theme.Label(hit.PageTitle, 14, true));
            if (hit.Location.Length > 0) content.Children.Add(theme.Label(hit.Location, 10, false, theme.Muted));
            content.Children.Add(new TextBlock { Text = hit.Snippet, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = OfficeTheme.Brush(theme.Muted), MaxLines = 3 });
            var button = new OfficeButton { Theme = theme, Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, "search-result-" + hit.PageId + "-" + (hit.BlockId ?? "title"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, hit.PageTitle + ": " + hit.Snippet);
            button.Click += (_, _) => ResultSelected?.Invoke(this, hit); results.Children.Add(button);
        }
    }
}

public sealed class NoteStatusBar : UserControl
{
    private readonly Grid root = new();
    private readonly TextBlock status = new() { FontSize = 11, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock percent = new() { FontSize = 11, Width = 38, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
    private readonly Slider slider = new() { Minimum = 25, Maximum = 250, Value = 100, Width = 100, Height = 26, StepFrequency = 5, SmallChange = 5, LargeChange = 25 };
    private bool updating;
    public event EventHandler<float>? ZoomRequested;
    public event EventHandler? FocusRequested;
    public NoteStatusBar()
    {
        Height = 28; root.ColumnDefinitions.Add(new ColumnDefinition()); root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); root.Children.Add(status);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(4, 0, 8, 0) };
        controls.Children.Add(new OfficeButton("−", "", () => ZoomRequested?.Invoke(this, (float)Math.Max(0.25, slider.Value / 100 - 0.1)), "Zoom out") { Width = 26, Height = 25 });
        controls.Children.Add(slider);
        controls.Children.Add(new OfficeButton("+", "", () => ZoomRequested?.Invoke(this, (float)Math.Min(2.5, slider.Value / 100 + 0.1)), "Zoom in") { Width = 26, Height = 25 }); controls.Children.Add(percent);
        controls.Children.Add(new OfficeButton("", "fullscreen", () => FocusRequested?.Invoke(this, EventArgs.Empty), "Full page view") { Width = 27, Height = 25 });
        Grid.SetColumn(controls, 1); root.Children.Add(controls); Content = root;
        slider.ValueChanged += (_, _) => { if (!updating) ZoomRequested?.Invoke(this, (float)slider.Value / 100); };
    }
    public void Update(string text, float zoom, OfficeTheme theme)
    {
        Background = OfficeTheme.Brush(theme.Panel); status.Text = text; status.Foreground = OfficeTheme.Brush(theme.Muted); percent.Foreground = OfficeTheme.Brush(theme.Text);
        percent.Text = $"{zoom * 100:0}%"; updating = true; slider.Value = zoom * 100; updating = false;
    }
}

public sealed class ColorPalette : UserControl
{
    public static IReadOnlyList<uint> Colors { get; } = [0xFF242424, 0xFF803AB3, 0xFF3167B3, 0xFF008A8A, 0xFF368145, 0xFFE8B226, 0xFFE78932, 0xFFCE4545, 0xFFD568A6, 0xFFFFFFFF];
    public event EventHandler<uint>? ColorSelected;
    public ColorPalette()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(8) };
        foreach (var color in Colors)
        {
            var button = new Button { Width = 26, Height = 26, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Background = OfficeTheme.Brush(color), BorderThickness = new Thickness(1), BorderBrush = OfficeTheme.Brush(0xFFB8B8B8), CornerRadius = new CornerRadius(13) };
            ToolTipService.SetToolTip(button, $"#{color & 0xFFFFFF:X6}"); button.Click += (_, _) => ColorSelected?.Invoke(this, color); panel.Children.Add(button);
        }
        Content = panel;
    }
}

public sealed class BackstageControl : UserControl
{
    public event EventHandler<string>? CommandInvoked;
    public BackstageControl() { Visibility = Visibility.Collapsed; }
    public void Show(Workspace workspace, OfficeTheme theme)
    {
        var grid = new Grid { Background = OfficeTheme.Brush(theme.Surface) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var side = new StackPanel { Spacing = 6, Background = OfficeTheme.Brush(OfficeTheme.Accent), Padding = new Thickness(16) };
        var back = new Button { Content = "←  Back", Foreground = OfficeTheme.Brush(0xFFFFFFFF), Background = OfficeTheme.Brush(0x00000000), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 44 }; back.Click += (_, _) => Visibility = Visibility.Collapsed; side.Children.Add(back);
        foreach (var (id, label) in new[] { ("new-notebook", "New notebook"), ("import", "Open / Import"), ("save", "Save"), ("export", "Export notebook"), ("markdown", "Export Markdown"), ("html", "Export HTML"), ("png", "Export PNG"), ("about", "About NoteSpace") })
        {
            var button = new Button { Content = label, Foreground = OfficeTheme.Brush(0xFFFFFFFF), Background = OfficeTheme.Brush(0x00000000), BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Height = 42 };
            button.Click += (_, _) => { Visibility = Visibility.Collapsed; CommandInvoked?.Invoke(this, id); }; side.Children.Add(button);
        }
        grid.Children.Add(side);
        var body = new StackPanel { Margin = new Thickness(42, 30, 32, 32), Spacing = 22 };
        body.Children.Add(theme.Label("Your notebooks", 32)); body.Children.Add(theme.Label("A place for everything you want to remember.", 15, false, theme.Muted));
        foreach (var notebook in workspace.Notebooks)
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18 };
            content.Children.Add(new NoteIcon { Glyph = "book", Width = 40, Height = 40, InkColor = notebook.Color });
            var text = new StackPanel { Spacing = 5 }; text.Children.Add(theme.Label(notebook.Title, 18, true)); text.Children.Add(theme.Label($"{notebook.Sections.Count} sections · {notebook.Sections.Sum(s => s.Pages.Count)} pages · Stored on this device", 12, false, theme.Muted)); content.Children.Add(text);
            body.Children.Add(new Border { Child = content, BorderThickness = new Thickness(1), BorderBrush = OfficeTheme.Brush(theme.Border), Padding = new Thickness(22), CornerRadius = new CornerRadius(6) });
        }
        body.Children.Add(new TextBlock { Text = "Your notebook data stays on this device. Export a .notespace backup to transfer it or keep another copy. Microsoft .one files and OneDrive synchronization are not supported.", TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = OfficeTheme.Brush(theme.Muted), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left });
        var scroll = new ScrollViewer { Content = body }; Grid.SetColumn(scroll, 1); grid.Children.Add(scroll); Content = grid; Visibility = Visibility.Visible;
    }
}
