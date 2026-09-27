using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface
{
    private TableCellAddress? editingCell;
    private TableCellAddress tableCell;
    public TableCellAddress? SelectedTableCell => SelectedBlock is { Kind: BlockKind.Table, Cells.Count: > 0 } b ? ClampCell(b, tableCell) : null;
    public bool IsEditingTableCell => editor is not null && editingCell.HasValue;
    private static TableCellAddress ClampCell(NoteBlock table, TableCellAddress cell) => new(
        Math.Clamp(cell.Row, 0, Math.Max(0, table.Cells.Count - 1)), Math.Clamp(cell.Column, 0, NoteTable.ColumnCount(table) - 1));

    public void BeginEditTableCell(NoteBlock table, TableCellAddress cell)
    {
        if (Page is null || session is null) return;
        EndEditing(); if (HasPendingText) return;
        // Reconcile IDs after flushing a draft, which can replace the page DTO.
        table = Page.Blocks.FirstOrDefault(b => b.Id == table.Id) ?? throw new InvalidOperationException("The table no longer exists.");
        if (table.Cells.Count == 0) { CommandRequested?.Invoke(this, new("edit-table", table.Id)); return; }
        ActivateTableCell(table, cell);
    }
    private void ActivateTableCell(NoteBlock table, TableCellAddress cell)
    {
        cell = ClampCell(table, cell);
        SelectedBlockId = table.Id; tableCell = cell; editingCell = cell;
        editingPageId = Page.Id; editingBlockId = table.Id; editingTitle = false;
        var text = NoteTable.GetCell(table, cell);
        var format = new TextFormat { FontSize = 14, Bold = cell.Row == 0 };
        if (editor is { } box)
        {
            // Native input is shared by the browser. Reuse its control rather than
            // detaching it during key dispatch and losing the new cell's focus.
            switchingCell = true;
            try { committedText = text; pendingText = false; box.Text = text; ApplyEditorStyle(box, format); box.Select(text.Length, 0); }
            finally { switchingCell = false; }
            AutomationProperties.SetName(box, $"Table row {cell.Row + 1}, column {cell.Column + 1}");
            box.Focus(FocusState.Programmatic);
        }
        else CreateEditor(text, format);
        // Keep the table visible beneath the single native cell editor.
        canvas.Options.EditingId = null;
        RevealTableCell(table, cell); Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void RevealTableCell(NoteBlock table, TableCellAddress cell)
    {
        var bounds = NoteTable.CellBounds(table, cell);
        var width = (float)Math.Max(50, ActualWidth / Zoom); var height = (float)Math.Max(50, ActualHeight / Zoom);
        if (bounds.X < canvas.Options.OffsetX) canvas.Options.OffsetX = Math.Max(0, bounds.X - 8);
        else if (bounds.X + bounds.Width > canvas.Options.OffsetX + width) canvas.Options.OffsetX = Math.Max(0, bounds.X + bounds.Width - width + 8);
        if (bounds.Y < canvas.Options.OffsetY) canvas.Options.OffsetY = Math.Max(0, bounds.Y - 8);
        else if (bounds.Y + bounds.Height > canvas.Options.OffsetY + height) canvas.Options.OffsetY = Math.Max(0, bounds.Y + bounds.Height - height + 8);
    }
    private void MoveTableCell(bool backwards, bool nextRow)
    {
        var cell = editingCell; var id = editingBlockId;
        if (cell is null || id is null || session is null) return;
        FlushPendingText(); if (HasPendingText || SelectedBlock is not { Kind: BlockKind.Table } table) return;
        var columns = NoteTable.ColumnCount(table);
        var next = nextRow ? new TableCellAddress(cell.Value.Row + (backwards ? -1 : 1), cell.Value.Column)
            : new TableCellAddress((cell.Value.Row * columns + cell.Value.Column + (backwards ? -1 : 1)) / columns,
                (cell.Value.Row * columns + cell.Value.Column + (backwards ? -1 : 1)) % columns);
        if (next.Row < 0 || next.Column < 0) { ActivateTableCell(table, new(0, 0)); return; }
        if (next.Row >= table.Cells.Count)
        {
            try { session.EditPage(Page!.Id, "Append table row", p => NoteTable.InsertRow(p.Blocks.First(b => b.Id == id), table.Cells.Count)); }
            catch (Exception error) { Error?.Invoke(this, error.Message); ActivateTableCell(SelectedBlock!, cell.Value); return; }
        }
        ActivateTableCell(SelectedBlock!, next);
    }
    public void EditSelectedTableCell()
    {
        if (SelectedBlock is not { Kind: BlockKind.Table } table) throw new InvalidOperationException("Select a table first.");
        BeginEditTableCell(table, tableCell);
    }
    public void ApplyTableCommand(string command)
    {
        EndEditing(); if (HasPendingText) throw new InvalidOperationException("Resolve the current draft before editing the table.");
        if (session is null || Page is null || SelectedBlock is not { Kind: BlockKind.Table } table) throw new InvalidOperationException("Select a table first.");
        var cell = ClampCell(table, tableCell); var pageId = Page.Id; var id = table.Id;
        session.EditPage(pageId, "Table " + command.Replace('-', ' '), page => {
            var target = page.Blocks.First(b => b.Id == id);
            switch (command)
            {
                case "row-above": NoteTable.InsertRow(target, cell.Row); break;
                case "row-below": NoteTable.InsertRow(target, cell.Row + 1); break;
                case "column-left": NoteTable.InsertColumn(target, cell.Column); break;
                case "column-right": NoteTable.InsertColumn(target, cell.Column + 1); break;
                case "delete-row": NoteTable.DeleteRow(target, cell.Row); break;
                case "delete-column": NoteTable.DeleteColumn(target, cell.Column); break;
                case "clear-cell": NoteTable.SetCell(target, cell, ""); break;
                case "transpose": NoteTable.Transpose(target); break;
                default: throw new ArgumentException("Unknown table command.", nameof(command));
            }
        });
        tableCell = ClampCell(SelectedBlock!, cell); Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public string CopyTableText()
    {
        FlushPendingText();
        if (HasPendingText) throw new InvalidOperationException("Resolve the current draft before copying the table.");
        return NoteTable.ToTsv(SelectedBlock ?? throw new InvalidOperationException("Select a table first."));
    }
    public void PasteTableText(string text)
    {
        EndEditing(); if (HasPendingText) throw new InvalidOperationException("Resolve the current draft before pasting.");
        if (session is null || Page is null || SelectedBlock is not { Kind: BlockKind.Table } table) throw new InvalidOperationException("Select a table first.");
        var cell = ClampCell(table, tableCell); var id = table.Id;
        session.EditPage(Page.Id, "Paste table cells", p => NoteTable.Paste(p.Blocks.First(b => b.Id == id), cell, text));
        Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
