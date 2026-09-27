using System.Text;
using NoteSpace.Core;

namespace NoteSpace.Editor;

public readonly record struct TableCellAddress(int Row, int Column);

/// <summary>UI-independent rectangular table operations. Cell/TSV limits are checked
/// before publishing a replacement grid. Wrap mutations in EditorSession.EditPage.</summary>
public static class NoteTable
{
    public const float RowHeight = 42;
    public const int MaximumRows = 476; // 476 * 42 fits the model's 20,000-pixel height.
    public const int MaximumColumns = 50;
    public const int MaximumCellLength = 100000;
    public const int MaximumTsvLength = 2 * 1024 * 1024;

    public static int ColumnCount(NoteBlock table) => Math.Max(1, table.Cells.Select(r => r.Count).DefaultIfEmpty(1).Max());
    public static NoteRect CellBounds(NoteBlock table, TableCellAddress cell)
    {
        CheckCell(table, cell);
        var width = table.Width / ColumnCount(table);
        return new(table.X + cell.Column * width, table.Y + cell.Row * RowHeight, width, RowHeight);
    }
    public static TableCellAddress? HitTest(NoteBlock table, float x, float y)
    {
        if (table.Kind != BlockKind.Table || table.Cells.Count == 0 || !table.Bounds.Contains(x, y)
            || x >= table.X + table.Width || y >= table.Y + table.Cells.Count * RowHeight) return null;
        return new((int)((y - table.Y) / RowHeight), Math.Min(ColumnCount(table) - 1, (int)((x - table.X) / (table.Width / ColumnCount(table)))));
    }
    public static string GetCell(NoteBlock table, TableCellAddress cell)
    {
        CheckCell(table, cell); return cell.Column < table.Cells[cell.Row].Count ? table.Cells[cell.Row][cell.Column] : "";
    }
    public static void SetCell(NoteBlock table, TableCellAddress cell, string value)
    {
        CheckCell(table, cell); CheckValue(value);
        var cells = Copy(table);
        cells[cell.Row][cell.Column] = value; table.Cells = cells;
    }
    public static void InsertRow(NoteBlock table, int at)
    {
        CheckTable(table);
        if (at < 0 || at > table.Cells.Count) throw new ArgumentOutOfRangeException(nameof(at));
        if (table.Cells.Count >= MaximumRows) throw new InvalidOperationException($"Tables support {MaximumRows} visible rows.");
        var cells = Copy(table); cells.Insert(at, Enumerable.Repeat("", ColumnCount(table)).ToList()); Publish(table, cells);
    }
    public static void DeleteRow(NoteBlock table, int at)
    {
        CheckCell(table, new(at, 0));
        if (table.Cells.Count <= 1) throw new InvalidOperationException("Keep at least one row, or delete the whole table.");
        var cells = Copy(table); cells.RemoveAt(at); Publish(table, cells);
    }
    public static void InsertColumn(NoteBlock table, int at)
    {
        CheckTable(table); var count = ColumnCount(table);
        if (at < 0 || at > count) throw new ArgumentOutOfRangeException(nameof(at));
        if (count >= MaximumColumns) throw new InvalidOperationException($"Tables support {MaximumColumns} columns.");
        var cells = Copy(table); foreach (var row in cells) row.Insert(at, ""); Publish(table, cells);
    }
    public static void DeleteColumn(NoteBlock table, int at)
    {
        CheckCell(table, new(0, at));
        if (ColumnCount(table) <= 1) throw new InvalidOperationException("Keep at least one column, or delete the whole table.");
        var cells = Copy(table); foreach (var row in cells) row.RemoveAt(at); Publish(table, cells);
    }
    public static void Transpose(NoteBlock table)
    {
        CheckTable(table);
        if (table.Cells.Count > MaximumColumns) throw new InvalidOperationException("Transposing would exceed 50 columns.");
        var source = Copy(table); var cells = new List<List<string>>();
        for (var c = 0; c < ColumnCount(table); c++) cells.Add(source.Select(r => r[c]).ToList());
        Publish(table, cells);
    }
    public static void Paste(NoteBlock table, TableCellAddress at, string text)
    {
        CheckCell(table, at); var incoming = ParseTsv(text);
        var rows = Math.Max(table.Cells.Count, checked(at.Row + incoming.Count));
        var columns = Math.Max(ColumnCount(table), checked(at.Column + incoming.Max(r => r.Count)));
        if (rows > MaximumRows || columns > MaximumColumns) throw new InvalidOperationException("Pasting would exceed the table dimensions.");
        var cells = Copy(table);
        while (cells.Count < rows) cells.Add([]);
        foreach (var row in cells) while (row.Count < columns) row.Add("");
        for (var r = 0; r < incoming.Count; r++) for (var c = 0; c < incoming[r].Count; c++) cells[at.Row + r][at.Column + c] = incoming[r][c];
        Publish(table, cells);
    }
    public static string ToTsv(NoteBlock table)
    {
        CheckTable(table); var result = new StringBuilder(); var columns = ColumnCount(table);
        for (var r = 0; r < table.Cells.Count; r++)
        {
            if (r > 0) result.Append('\n');
            for (var c = 0; c < columns; c++)
            {
                if (c > 0) result.Append('\t');
                var value = c < table.Cells[r].Count ? table.Cells[r][c] : "";
                if (value.Length == 0 || value.IndexOfAny(['\t', '\n', '\r', '"']) >= 0) result.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                else result.Append(value);
                if (result.Length > MaximumTsvLength) throw new InvalidDataException("Table clipboard content exceeds 2 MiB characters.");
            }
        }
        return result.ToString();
    }
    public static List<List<string>> ParseTsv(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumTsvLength) throw new InvalidDataException("Table clipboard content exceeds 2 MiB characters.");
        var rows = new List<List<string>>(); var row = new List<string>(); var value = new StringBuilder();
        var quoted = false; var closed = false; var endedRow = false;
        void Cell()
        {
            if (row.Count >= MaximumColumns) throw new InvalidDataException("Too many table columns.");
            row.Add(value.ToString()); value.Clear(); closed = false;
        }
        void Row()
        {
            if (rows.Count >= MaximumRows) throw new InvalidDataException("Too many table rows.");
            rows.Add(row); row = [];
        }
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i]; endedRow = false;
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else value.Append(ch);
            }
            else if (ch == '\t') Cell();
            else if (ch is '\r' or '\n')
            {
                Cell(); Row(); endedRow = true;
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (closed) throw new InvalidDataException("Unexpected content after a quoted table cell.");
            else if (ch == '"' && value.Length == 0) quoted = true;
            else value.Append(ch);
            if (value.Length > MaximumCellLength) throw new InvalidDataException("A table cell exceeds 100,000 characters.");
        }
        if (quoted) throw new InvalidDataException("Unclosed quoted table cell.");
        if (!endedRow || text.Length == 0) { Cell(); Row(); }
        var columns = rows.Max(r => r.Count);
        foreach (var cells in rows) while (cells.Count < columns) cells.Add("");
        return rows;
    }
    private static List<List<string>> Copy(NoteBlock table)
    {
        CheckTable(table); var columns = ColumnCount(table);
        return table.Cells.Select(r => r.Concat(Enumerable.Repeat("", columns - r.Count)).ToList()).ToList();
    }
    private static void Publish(NoteBlock table, List<List<string>> cells)
    {
        table.Cells = cells; table.Height = Math.Clamp(cells.Count * RowHeight, RowHeight, 20000);
    }
    private static void CheckValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumCellLength) throw new InvalidDataException("A table cell exceeds 100,000 characters.");
    }
    private static void CheckTable(NoteBlock table)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (table.Kind != BlockKind.Table || table.Cells.Count == 0) throw new ArgumentException("Select a non-empty table.", nameof(table));
        if (table.Cells.Count > 500 || ColumnCount(table) > MaximumColumns) throw new InvalidDataException("Invalid table dimensions.");
    }
    private static void CheckCell(NoteBlock table, TableCellAddress cell)
    {
        CheckTable(table);
        if (cell.Row < 0 || cell.Row >= table.Cells.Count || cell.Column < 0 || cell.Column >= ColumnCount(table)) throw new ArgumentOutOfRangeException(nameof(cell));
    }
}
