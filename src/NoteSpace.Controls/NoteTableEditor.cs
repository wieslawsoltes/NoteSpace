using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.Controls;

/// <summary>Independent, transactional table editor. Call GetCells only when accepting the edit.</summary>
public sealed class NoteTableEditor : UserControl
{
    private readonly List<List<string>> cells;
    private readonly OfficeTheme theme;
    private readonly Grid grid = new();
    private readonly StackPanel root = new() { Spacing = 8 };
    private readonly StackPanel tools = new() { Orientation = Orientation.Horizontal, Spacing = 5 };
    private TextBox? largeTable;
    public NoteTableEditor(IEnumerable<IEnumerable<string>> rows, OfficeTheme palette)
    {
        theme = palette; cells = rows.Select(r => r.ToList()).ToList();
        if (cells.Count == 0) cells.AddRange([new List<string> { "Heading 1", "Heading 2", "Heading 3" }, new List<string> { "", "", "" }, new List<string> { "", "", "" }]);
        var columns = Math.Max(1, cells.Max(r => r.Count)); foreach (var row in cells) while (row.Count < columns) row.Add("");
        tools.Children.Add(new OfficeButton("Add row", "add", () => { if (cells.Count >= 476) return; cells.Add(Enumerable.Repeat("", cells[0].Count).ToList()); Rebuild(); }, theme: theme));
        tools.Children.Add(new OfficeButton("Add column", "add", () => { if (cells[0].Count >= 50) return; foreach (var row in cells) row.Add(""); Rebuild(); }, theme: theme));
        tools.Children.Add(new OfficeButton("Remove row", "", () => { if (cells.Count > 1) { cells.RemoveAt(cells.Count - 1); Rebuild(); } }, theme: theme));
        tools.Children.Add(new OfficeButton("Remove column", "", () => { if (cells[0].Count > 1) { foreach (var row in cells) row.RemoveAt(row.Count - 1); Rebuild(); } }, theme: theme));
        root.Children.Add(tools);
        root.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 430, MaxWidth = 820 });
        root.Children.Add(theme.Label("The first row is the header. Changes are applied together when you choose Save.", 11, false, theme.Muted));
        Content = root; Rebuild();
    }
    public List<List<string>> GetCells()
    {
        if (largeTable is not null) return NoteTable.ParseTsv(largeTable.Text);
        var snapshot = cells.Select(r => r.ToList()).ToList();
        foreach (var box in grid.Children.OfType<TextBox>()) snapshot[Grid.GetRow(box)][Grid.GetColumn(box)] = box.Text;
        return snapshot;
    }
    private void Rebuild()
    {
        grid.Children.Clear(); grid.RowDefinitions.Clear(); grid.ColumnDefinitions.Clear();
        var isLarge = cells.Count * cells[0].Count > 400;
        // In TSV mode the text is the working copy. Do not rebuild it from stale grid cells.
        // Row/column edits remain available by editing the TSV itself, then saving.
        tools.IsHitTestVisible = !isLarge;
        tools.Opacity = isLarge ? 0.45 : 1;
        foreach (var child in tools.Children.OfType<Control>()) child.IsEnabled = !isLarge;
        if (isLarge)
        {
            largeTable = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Text = NoteTable.ToTsv(new NoteBlock { Kind = BlockKind.Table, Cells = cells }), Width = 740, Height = 380, FontSize = 13, MaxLength = 2 * 1024 * 1024, Header = "Large table — tab-separated cells, one row per line" };
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
                box.TextChanging += (_, _) => cells[row][column] = box.Text;
                Grid.SetRow(box, r); Grid.SetColumn(box, c); grid.Children.Add(box);
            }
        }
    }
}
