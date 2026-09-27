using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NoteSpace.Core;
using NoteSpace.Editor;
using Windows.System;
using Windows.UI.Core;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface
{
    private bool switchingCell;
    public void ToggleFormat(Func<TextFormat, bool> read, Action<TextFormat, bool> write)
    {
        FlushPendingText();
        if (HasPendingText || SelectedBlock is not { } block) return;
        if (block.Kind == BlockKind.Table) throw new InvalidOperationException("Table cells use plain text. Select a text note for range formatting.");
        var start = editor is not null && !editingTitle ? editor.SelectionStart : 0;
        var length = editor is not null && !editingTitle ? editor.SelectionLength : 0;
        var enabled = !RichText.AllHave(block, start, length, read);
        FormatSelection(format => write(format, enabled));
    }

    public void FormatSelection(Action<TextFormat> apply, string? link = null)
    {
        FlushPendingText(); if (HasPendingText || SelectedBlock is not { } block || session is null || Page is null) return;
        if (block.Kind == BlockKind.Table) throw new InvalidOperationException("Table cells use plain text. Select a text note for range formatting.");
        var start = editor is not null && !editingTitle ? editor.SelectionStart : 0;
        var length = editor is not null && !editingTitle ? editor.SelectionLength : 0;
        session.EditPage(Page.Id, "Format text", p => {
            var b = p.Blocks.First(b => b.Id == block.Id); RichText.Apply(b, start, length, apply, link);
            b.Height = Math.Max(b.Height, Renderer.MeasureHeight(b));
        });
        if (editor is not null) ApplyEditorStyle(editor, SelectedBlock?.Format ?? block.Format);
        Refresh();
    }
    public void InsertText(string text)
    {
        if (Page is null) return;
        if (editor is null) { if (SelectedBlock is { Kind: BlockKind.Text or BlockKind.Heading or BlockKind.Checklist } b) BeginEdit(b); else NewText(); }
        if (editor is null) return;
        var start = editor.SelectionStart; var length = editor.SelectionLength;
        editor.Text = editor.Text[..start] + text + editor.Text[(start + length)..]; editor.SelectionStart = start + text.Length; editor.SelectionLength = 0;
        editor.Focus(FocusState.Programmatic);
    }
    public void BeginEditTitle()
    {
        if (Page is null) return; EndEditing(); if (HasPendingText) return; editingPageId = Page.Id; editingTitle = true; editingBlockId = null;
        CreateEditor(Page.Title, new TextFormat { FontSize = 32 }); canvas.Options.EditingTitle = true;
        PositionEditor(); canvas.Invalidate();
    }
    public void BeginEdit(NoteBlock block)
    {
        if (Page is null) return;
        if (block.Kind == BlockKind.Table) { BeginEditTableCell(block, tableCell); return; }
        if (block.Kind is BlockKind.Attachment or BlockKind.Image) { CommandRequested?.Invoke(this, new("save-attachment", block.Id)); return; }
        if (block.Kind == BlockKind.Divider) return;
        if (editingBlockId == block.Id && editor is not null) { editor.Focus(FocusState.Programmatic); return; }
        EndEditing(); if (pendingText) return;
        SelectedBlockId = block.Id; canvas.Options.SelectedId = block.Id;
        editingPageId = Page.Id; editingBlockId = block.Id; editingTitle = false;
        CreateEditor(block.Text, block.Format); canvas.Options.EditingId = block.Id;
        PositionEditor(); canvas.Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void CreateEditor(string text, TextFormat format)
    {
        var box = new NoteInputBox { Text = text, AcceptsReturn = !editingTitle, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(1), BorderBrush = OfficeTheme.Brush(OfficeTheme.Accent), Background = OfficeTheme.Brush(Dark ? 0xFF252525 : 0xFFFFFFFF), Padding = new Thickness(11, 9, 11, 8), MinWidth = 40, MinHeight = 32, MaxLength = editingTitle ? 500 : editingCell.HasValue ? NoteTable.MaximumCellLength : 2 * 1024 * 1024, IsSpellCheckEnabled = true };
        editor = box; committedText = text; pendingText = false; ApplyEditorStyle(box, format);
        AutomationProperties.SetName(box, editingTitle ? "Page title" : editingCell is { } cell ? $"Table row {cell.Row + 1}, column {cell.Column + 1}" : "Note text");
        AutomationProperties.SetAutomationId(box, editingTitle ? "page-title-editor" : editingCell.HasValue ? "table-cell-editor" : "note-text-editor");
        // TextChanged can be coalesced by the native/Skia text bridge. Track the
        // synchronous change as well, and always reconcile the actual value at commit.
        box.TextChanging += (_, _) => {
            if (editor != box || switchingCell) return;
            pendingText = box.Text != committedText;
            typingTimer.Stop(); if (pendingText) typingTimer.Start();
            DraftChanged?.Invoke(this, EventArgs.Empty);
        };
        box.TextChanged += (_, _) => { if (editor == box) PositionEditor(); };
        box.HandleKey = e => {
            if (editor != box) return false;
            var backwards = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
            var controlDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
            if (editingCell.HasValue && (e.Key == VirtualKey.Tab || e.Key == VirtualKey.Enter && !controlDown))
            {
                var nextRow = e.Key == VirtualKey.Enter;
                DispatcherQueue.TryEnqueue(() => { if (editor == box) MoveTableCell(backwards, nextRow); });
                return true;
            }
            if (e.Key == VirtualKey.Escape || editingTitle && e.Key == VirtualKey.Enter) { EndEditing(); return true; }
            return false;
        };
        box.LostFocus += (_, _) => { if (editor == box) FlushPendingText(); };
        void QueueFocus() => DispatcherQueue.TryEnqueue(() => {
            if (editor == box && box.IsLoaded) box.Focus(FocusState.Programmatic);
        });
        // Register before adding to an already loaded tree, then focus after the
        // initiating accelerator/pointer event and layout have completed.
        box.Loaded += (_, _) => QueueFocus();
        overlay.Children.Add(box); box.SelectionStart = box.Text.Length;
        QueueFocus();
    }
    private void ApplyEditorStyle(TextBox box, TextFormat format)
    {
        box.FontSize = format.FontSize * Zoom; box.FontFamily = new FontFamily(format.FontFamily);
        box.FontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)(format.Bold ? 700 : 400) };
        box.FontStyle = format.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
        box.Foreground = OfficeTheme.Brush(Dark && format.Color == 0xFF242424 ? 0xFFF2F2F2 : format.Color);
        box.TextAlignment = format.Alignment == 1 ? TextAlignment.Center : format.Alignment == 2 ? TextAlignment.Right : TextAlignment.Left;
    }
    public void FlushPendingText()
    {
        if (updating) return;
        typingTimer.Stop();
        if (editor is null || session is null || editingPageId is null) return;
        var box = editor; var pageId = editingPageId; var blockId = editingBlockId;
        var text = box.Text; var title = editingTitle; var cell = editingCell;
        // A notification is a scheduling hint, never proof that input is unchanged.
        // Read the current TextBox value before navigation, export, or removal.
        if (text == committedText) { pendingText = false; return; }
        pendingText = true; updating = true;
        var committed = false;
        try
        {
            if (session.FindPage(pageId) is null) throw new InvalidOperationException("The page being edited no longer exists.");
            if (title) session.RenamePage(pageId, text);
            else if (blockId is not null) session.EditPage(pageId, "Edit text", p => {
                var b = p.Blocks.FirstOrDefault(b => b.Id == blockId) ?? throw new InvalidOperationException("The note being edited no longer exists.");
                if (cell.HasValue) NoteTable.SetCell(b, cell.Value, text);
                else { RichText.Replace(b, text); b.Height = Math.Clamp(Renderer.MeasureHeight(b), 40, 20000); }
            });
            if (editor == box) committedText = text;
            committed = true;
        }
        catch (Exception e) { Error?.Invoke(this, e.Message); }
        finally
        {
            updating = false;
            pendingText = editor is not null && editor.Text != committedText;
            if (committed && pendingText) typingTimer.Start();
        }
        Refresh();
    }
    public void EndEditing() { FlushPendingText(); if (!updating && !HasPendingText) CancelEditor(); }
    private void CancelEditor()
    {
        typingTimer.Stop(); var old = editor; editor = null;
        if (old is not null) overlay.Children.Remove(old);
        editingPageId = null; editingBlockId = null; editingTitle = false; editingCell = null; pendingText = false; committedText = "";
        canvas.Options.EditingId = null; canvas.Options.EditingTitle = false; canvas.Invalidate();
    }
    private void PositionEditor()
    {
        if (editor is null) return;
        var b = Page?.Blocks.FirstOrDefault(b => b.Id == editingBlockId);
        if (editingCell is { } cell && b is { Kind: BlockKind.Table })
        {
            var bounds = NoteTable.CellBounds(b, cell);
            Canvas.SetLeft(editor, (bounds.X - canvas.Options.OffsetX) * Zoom); Canvas.SetTop(editor, (bounds.Y - canvas.Options.OffsetY) * Zoom);
            editor.Width = Math.Max(24, bounds.Width * Zoom); editor.Height = NoteTable.RowHeight * Zoom; editor.FontSize = 14 * Zoom; return;
        }
        var x = editingTitle ? 38 : b?.X ?? 48; var y = editingTitle ? 23 : b?.Y ?? 140;
        var width = editingTitle ? 672 : b?.Width ?? 560; var size = editingTitle ? 32 : b?.Format.FontSize ?? 16;
        var lines = editingTitle ? 1 : editor.Text.Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length * size * 0.55 / Math.Max(50, width - 24))));
        var height = editingTitle ? 61 : Math.Max(b?.Height ?? 60, lines * size * 1.6f + 28);
        Canvas.SetLeft(editor, (x - canvas.Options.OffsetX) * Zoom); Canvas.SetTop(editor, (y - canvas.Options.OffsetY) * Zoom);
        editor.Width = Math.Max(40, width * Zoom); editor.Height = Math.Max(32, Math.Min(20000, height) * Zoom); editor.FontSize = size * Zoom;
    }
}
