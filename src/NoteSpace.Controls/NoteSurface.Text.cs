using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NoteSpace.Core;
using NoteSpace.Editor;
using Windows.System;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface
{
    public void ToggleFormat(Func<TextFormat, bool> read, Action<TextFormat, bool> write)
    {
        FlushPendingText();
        if (SelectedBlock is not { } block) return;
        var start = editor is not null && !editingTitle ? editor.SelectionStart : 0;
        var length = editor is not null && !editingTitle ? editor.SelectionLength : 0;
        var enabled = !RichText.AllHave(block, start, length, read);
        FormatSelection(format => write(format, enabled));
    }

    public void FormatSelection(Action<TextFormat> apply, string? link = null)
    {
        FlushPendingText(); if (SelectedBlock is not { } block || session is null || Page is null) return;
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
        if (Page is null) return; EndEditing(); editingPageId = Page.Id; editingTitle = true; editingBlockId = null;
        CreateEditor(Page.Title, new TextFormat { FontSize = 32 }); canvas.Options.EditingTitle = true;
        PositionEditor(); canvas.Invalidate();
    }
    public void BeginEdit(NoteBlock block)
    {
        if (Page is null) return;
        if (block.Kind == BlockKind.Table) { CommandRequested?.Invoke(this, new("edit-table", block.Id)); return; }
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
        var box = new TextBox { Text = text, AcceptsReturn = !editingTitle, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(1), BorderBrush = OfficeTheme.Brush(OfficeTheme.Accent), Background = OfficeTheme.Brush(Dark ? 0xFF252525 : 0xFFFFFFFF), Padding = new Thickness(11, 9, 11, 8), MinWidth = 40, MinHeight = 32, MaxLength = editingTitle ? 500 : 2 * 1024 * 1024, IsSpellCheckEnabled = true };
        editor = box; ApplyEditorStyle(box, format);
        AutomationProperties.SetName(box, editingTitle ? "Page title" : "Note text"); AutomationProperties.SetAutomationId(box, editingTitle ? "page-title-editor" : "note-text-editor");
        box.TextChanged += (_, _) => { if (updating || editor != box) return; pendingText = true; typingTimer.Stop(); typingTimer.Start(); PositionEditor(); DraftChanged?.Invoke(this, EventArgs.Empty); };
        box.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape || editingTitle && e.Key == VirtualKey.Enter) { EndEditing(); e.Handled = true; } };
        box.LostFocus += (_, _) => { if (editor == box) FlushPendingText(); };
        overlay.Children.Add(box); box.SelectionStart = box.Text.Length;
        box.Loaded += (_, _) => box.Focus(FocusState.Programmatic); box.Focus(FocusState.Programmatic);
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
        typingTimer.Stop(); if (!pendingText || editor is null || session is null || editingPageId is null) return;
        var pageId = editingPageId; var blockId = editingBlockId; var text = editor.Text; var title = editingTitle;
        pendingText = false; updating = true;
        try
        {
            if (session.FindPage(pageId) is null) return;
            if (title) session.RenamePage(pageId, text);
            else if (blockId is not null) session.EditPage(pageId, "Edit text", p => {
                var b = p.Blocks.FirstOrDefault(b => b.Id == blockId); if (b is null) return;
                RichText.Replace(b, text); b.Height = Math.Clamp(Renderer.MeasureHeight(b), 40, 20000);
            });
        }
        catch (Exception e) { pendingText = true; Error?.Invoke(this, e.Message); }
        finally { updating = false; }
        Refresh();
    }
    public void EndEditing() { FlushPendingText(); if (!pendingText) CancelEditor(); }
    private void CancelEditor()
    {
        typingTimer.Stop(); var old = editor; editor = null;
        if (old is not null) overlay.Children.Remove(old);
        editingPageId = null; editingBlockId = null; editingTitle = false; pendingText = false;
        canvas.Options.EditingId = null; canvas.Options.EditingTitle = false; canvas.Invalidate();
    }
    private void PositionEditor()
    {
        if (editor is null) return;
        var b = Page?.Blocks.FirstOrDefault(b => b.Id == editingBlockId);
        var x = editingTitle ? 38 : b?.X ?? 48; var y = editingTitle ? 23 : b?.Y ?? 140;
        var width = editingTitle ? 672 : b?.Width ?? 560; var size = editingTitle ? 32 : b?.Format.FontSize ?? 16;
        var lines = editingTitle ? 1 : editor.Text.Split('\n').Sum(l => Math.Max(1, (int)Math.Ceiling(l.Length * size * 0.55 / Math.Max(50, width - 24))));
        var height = editingTitle ? 61 : Math.Max(b?.Height ?? 60, lines * size * 1.6f + 28);
        Canvas.SetLeft(editor, (x - canvas.Options.OffsetX) * Zoom); Canvas.SetTop(editor, (y - canvas.Options.OffsetY) * Zoom);
        editor.Width = Math.Max(40, width * Zoom); editor.Height = Math.Max(32, Math.Min(20000, height) * Zoom); editor.FontSize = size * Zoom;
    }
}
