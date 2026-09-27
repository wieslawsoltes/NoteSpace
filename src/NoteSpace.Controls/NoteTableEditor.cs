using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NoteSpace.Controls;

/// <summary>Independent, transactional table editor. Call GetCells only when accepting the edit.</summary>
public sealed class NoteTableEditor : UserControl
{
    private readonly List<List<string>> cells;
    private readonly OfficeTheme theme;
    private readonly Grid grid = new();
    private readonly StackPanel root = new() { Spacing = 8 };
    private TextBox? largeTable;
    public NoteTableEditor(IEnumerable<IEnumerable<string>> rows, OfficeTheme palette)
    {
        theme = palette; cells = rows.Select(r => r.ToList()).ToList();
        if (cells.Count == 0) cells.AddRange([new List<string> { "Heading 1", "Heading 2", "Heading 3" }, new List<string> { "", "", "" }, new List<string> { "", "", "" }]);
        var columns = Math.Max(1, cells.Max(r => r.Count)); foreach (var row in cells) while (row.Count < columns) row.Add("");
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        tools.Children.Add(new OfficeButton("Add row", "add", () => { if (cells.Count >= 500) return; cells.Add(Enumerable.Repeat("", cells[0].Count).ToList()); Rebuild(); }, theme: theme));
        tools.Children.Add(new OfficeButton("Add column", "add", () => { if (cells[0].Count >= 50) return; foreach (var row in cells) row.Add(""); Rebuild(); }, theme: theme));
        tools.Children.Add(new OfficeButton("Remove row", "", () => { if (cells.Count > 1) { cells.RemoveAt(cells.Count - 1); Rebuild(); } }, theme: theme));
        tools.Children.Add(new OfficeButton("Remove column", "", () => { if (cells[0].Count > 1) { foreach (var row in cells) row.RemoveAt(row.Count - 1); Rebuild(); } }, theme: theme));
        root.Children.Add(tools);
        root.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 430, MaxWidth = 820 });
        root.Children.Add(theme.Label("The first row is the header. Changes are applied together when you choose Save.", 11, false, theme.Muted));
        Content = root; Rebuild();
    }
    public List<List<string>> GetCells() => largeTable is not null ? largeTable.Text.Replace("\r\n", "\n").Split('\n').Select(r => r.Split('\t').ToList()).ToList() : cells.Select(r => r.ToList()).ToList();
    private void Rebuild()
    {
        grid.Children.Clear(); grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear();
        if (cells.Count * cells[0].Count > 400)
        {
            largeTable = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Text = string.Join("\n", cells.Select(r => string.Join("\t", r))), Width = 740, Height = 380, FontSize = 13, Header = "Large table — tab-separated cells, one row per line" };
            grid.Children.Add(largeTable); return;
        }
        largeTable = null;
        for (var c = 0; c < cells[0].Count; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(155) });
        for (var r = 0; r < cells.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < cells[r].Count; c++)
            {
                var row = r; var column = c;
                var box = new TextBox { Text = cells[r][c], MinHeight = 40, FontSize = 13, Padding = new Thickness(8), BorderBrush = OfficeTheme.Brush(theme.Border), BorderThickness = new Thickness(0.5), Background = OfficeTheme.Brush(r == 0 ? theme.Selection : theme.Surface), Foreground = OfficeTheme.Brush(theme.Text), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 100000 };
                box.TextChanged += (_, _) => cells[row][column] = box.Text;
                Grid.SetRow(box, r); Grid.SetColumn(box, c); grid.Children.Add(box);
            }
        }
    }
}
