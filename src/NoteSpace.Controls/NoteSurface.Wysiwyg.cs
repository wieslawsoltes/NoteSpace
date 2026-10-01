using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Rendering.Skia;
using Windows.System;
using Windows.UI.Core;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface
{
    private TextEditingBuffer? richDraft;
    private TextEditVisual? richVisual;
    private long committedRichVersion;
    private bool syncingRichSelection;
    private float? preferredCaretX;
    private int pendingRichKeys;
    private readonly Queue<Action> richKeyActions = new();
    private bool drainingRichKeys;
    private TextFormat? copiedFormat;
    private NoteBlock? copiedText;
    private readonly DispatcherTimer caretTimer = new() { Interval = TimeSpan.FromMilliseconds(530) };
    public bool IsRichTextEditing => richDraft is not null;
    public bool IsRichTextFocused => richDraft is not null && editor is { FocusState: not FocusState.Unfocused };
    public int TextSelectionStart => richDraft?.SelectionStart ?? editor?.SelectionStart ?? 0;
    public int TextSelectionLength => richDraft?.SelectionLength ?? editor?.SelectionLength ?? 0;
    public TextFormat CurrentTextFormat => richDraft?.CurrentFormat ?? RichText.CloneStyle(SelectedBlock?.Format ?? new());
    public void FocusTextEditor()
    {
        var box = editor;
        DispatcherQueue.TryEnqueue(() => { if (editor == box && box is not null && box.IsLoaded) box.Focus(FocusState.Programmatic); });
    }
    public bool TextHasAttribute(Func<TextFormat, bool> predicate) => richDraft?.AllHave(predicate) ?? (SelectedBlock is { } block && RichText.AllHave(block, 0, 0, predicate));
    public bool HasCopiedFormat => copiedFormat is not null;
    public TextFlowSettings CurrentTextFlow => (richDraft?.Block.TextFlow ?? SelectedBlock?.TextFlow ?? new()).Copy();

    private void StartRichEditing(NoteBlock block)
    {
        richDraft = new(block);
        richDraft.NormalizeLineEndings();
        committedRichVersion = richDraft.Version;
        richVisual = new(richDraft.Block); canvas.Options.TextEdit = richVisual;
        preferredCaretX = null;
    }
    private void StopRichEditing()
    {
        richKeyActions.Clear(); pendingRichKeys = 0;
        caretTimer.Stop(); richDraft = null; richVisual = null; canvas.Options.TextEdit = null; preferredCaretX = null;
    }
    private void ConfigureRichInput(TextBox box)
    {
        if (richDraft is null) return;
        // This control is solely the native Unicode/clipboard/IME input buffer.
        // It never supplies visual text metrics or pointer hit testing. Keep it at
        // the shared-layout caret so native candidate windows have an anchor.
        box.TextWrapping = TextWrapping.NoWrap; box.IsHitTestVisible = false;
        box.Opacity = 0; box.Padding = new Thickness(0); box.BorderThickness = new Thickness(0);
        // WinUI/Uno requires IsTabStop even for programmatic Focus. Suppress Tab
        // traversal in HandleRichKey, not by making the input adapter unfocusable.
        box.MinWidth = 1; box.MinHeight = 1; box.IsTabStop = true;
        box.Foreground = OfficeTheme.Brush(0); box.Background = OfficeTheme.Brush(0);
        box.SelectionChanged += (_, _) => {
            if (editor != box || richDraft is null || syncingRichSelection || switchingCell || pendingRichKeys > 0 || drainingRichKeys) return;
            var start = box.SelectionStart; var end = start + box.SelectionLength;
            var same = start == richDraft.SelectionStart && box.SelectionLength == richDraft.SelectionLength;
            if (!same) richDraft.Select(start, end);
            UpdateRichAdorners(); SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        box.GotFocus += (_, _) => { if (editor == box && richDraft is not null) { caretTimer.Start(); UpdateRichAdorners(); } };
        box.LostFocus += (_, _) => { caretTimer.Stop(); if (richVisual is not null) { richVisual.CaretVisible = false; canvas.Invalidate(); } };
        caretTimer.Start();
    }
    // Uno's managed TextBox uses CR while its DOM textarea uses LF. Keep one
    // canonical rich draft so native normalization cannot flatten intervening runs.
    private static string NormalizeNativeText(string value) => value.Contains('\r')
        ? value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n') : value;
    private void RichNativeTextChanged(TextBox box)
    {
        if (richDraft is null || editor != box || switchingCell) return;
        try { richDraft.AcceptText(NormalizeNativeText(box.Text)); }
        catch (Exception error) { pendingText = true; Error?.Invoke(this, error.Message); }
        pendingText = box.Text != committedText || richDraft.Version != committedRichVersion;
        preferredCaretX = null; UpdateRichAdorners();
    }
    private void UpdateRichAdorners(bool reveal = false)
    {
        if (richDraft is null || richVisual is null || editor is null) return;
        var block = richDraft.Block;
        block.Height = Math.Clamp(Renderer.MeasureHeight(block), 40, 20000);
        richVisual.Selection = Renderer.SelectionBounds(block, richDraft.SelectionStart, richDraft.SelectionLength);
        richVisual.Caret = Renderer.CaretBounds(block, richDraft.Caret);
        richVisual.CaretVisible = editor.FocusState != FocusState.Unfocused;
        if (reveal)
        {
            var r = richVisual.Caret; var top = block.Y + r.Y; var left = block.X + r.X;
            var h = (float)Math.Max(50, ActualHeight / Zoom); var w = (float)Math.Max(50, ActualWidth / Zoom);
            if (top < canvas.Options.OffsetY + 8) canvas.Options.OffsetY = Math.Max(0, top - 8);
            else if (top + r.Height > canvas.Options.OffsetY + h - 8) canvas.Options.OffsetY = Math.Clamp(top + r.Height - h + 8, 0, 100000);
            if (left < canvas.Options.OffsetX + 8) canvas.Options.OffsetX = Math.Max(0, left - 8);
            else if (left > canvas.Options.OffsetX + w - 8) canvas.Options.OffsetX = Math.Clamp(left - w + 16, 0, 100000);
        }
        PositionRichInput(); canvas.Invalidate();
    }
    private void PositionRichInput()
    {
        if (richDraft is null || richVisual is null || editor is null) return;
        var b = richDraft.Block; var r = richVisual.Caret;
        Canvas.SetLeft(editor, (b.X + r.X - canvas.Options.OffsetX) * Zoom);
        Canvas.SetTop(editor, (b.Y + r.Y - canvas.Options.OffsetY) * Zoom);
        editor.Width = 1; editor.Height = Math.Max(1, r.Height * Zoom);
    }
    private void SetRichSelection(int anchor, int caret, bool preserveStyle = false)
    {
        if (richDraft is null || editor is null) return;
        richDraft.Select(anchor, caret, preserveStyle); syncingRichSelection = true;
        try { editor.Select(richDraft.SelectionStart, richDraft.SelectionLength); }
        finally { syncingRichSelection = false; }
        UpdateRichAdorners(true); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void SyncRichInput()
    {
        if (richDraft is null || editor is null) return;
        switchingCell = true; syncingRichSelection = true;
        try
        {
            if (editor is NoteInputBox input) input.SetHostText(richDraft.Block.Text, richDraft.SelectionStart, richDraft.SelectionLength);
            else { editor.Text = richDraft.Block.Text; editor.Select(richDraft.SelectionStart, richDraft.SelectionLength); }
        }
        finally { switchingCell = false; syncingRichSelection = false; }
        pendingText = richDraft.Version != committedRichVersion;
        typingTimer.Stop(); if (pendingText) typingTimer.Start();
        UpdateRichAdorners(true); DraftChanged?.Invoke(this, EventArgs.Empty); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    private void FlushRichDraft()
    {
        DrainRichKeys();
        typingTimer.Stop();
        if (richDraft is null || editor is null || session is null || editingPageId is null) return;
        try
        {
            richDraft.AcceptText(NormalizeNativeText(editor.Text));
            if (richDraft.Version == committedRichVersion) { pendingText = false; return; }
            var version = richDraft.Version; var draft = richDraft;
            updating = true;
            session.EditPage(editingPageId, "Edit rich text", page => {
                var target = page.Blocks.FirstOrDefault(b => b.Id == draft.Block.Id) ?? throw new InvalidOperationException("The edited note no longer exists.");
                draft.CopyContentTo(target); target.Height = Math.Clamp(Renderer.MeasureHeight(target), 40, 20000);
            });
            committedRichVersion = version; committedText = editor.Text; pendingText = false;
        }
        catch (Exception error) { pendingText = true; Error?.Invoke(this, error.Message); }
        finally { updating = false; }
        Refresh();
    }
    private void ReconcileRichDocument()
    {
        if (updating || richDraft is null || editor is null || Page is null) return;
        var block = Page.Blocks.FirstOrDefault(b => b.Id == editingBlockId);
        if (block is null || block.Kind is not (BlockKind.Text or BlockKind.Heading or BlockKind.Checklist)) { CancelEditor(); return; }
        var anchor = richDraft.Anchor; var caret = richDraft.Caret;
        StartRichEditing(block); richDraft!.Select(anchor, caret); committedText = block.Text;
        SyncRichInput(); committedText = editor.Text;
    }
    // Native DOM input can deliver the next key/accelerator before a dispatcher
    // callback runs. Drain earlier editing keys before accepting another command.
    // Dispatcher callbacks only drain this queue; they cannot replay an old action.
    private void DrainRichKeys()
    {
        if (drainingRichKeys) return;
        drainingRichKeys = true;
        try
        {
            while (richKeyActions.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception error) { pendingText = true; Error?.Invoke(this, error.Message); }
                finally { pendingRichKeys = richKeyActions.Count; }
            }
        }
        finally { drainingRichKeys = false; }
    }
    private bool HandleRichKey(KeyRoutedEventArgs e)
    {
        DrainRichKeys();
        if (richDraft is null || editor is null) return false;
        var shiftDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        var ctrlDown = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;
        var key = e.Key; var draft = richDraft; var box = editor;
        if (key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Back or VirtualKey.Delete or VirtualKey.Tab or VirtualKey.Enter)) return false;
        // Run after native dispatch. NoteInputBox suppresses default text writes for
        // these host-owned keys before this queued action updates the input buffer.
        pendingRichKeys++;
        richKeyActions.Enqueue(() => {
            if (richDraft != draft || editor != box) return;
            if (key is VirtualKey.Back or VirtualKey.Delete) { draft.Delete(key == VirtualKey.Back, ctrlDown); preferredCaretX = null; SyncRichInput(); return; }
            if (key is VirtualKey.Tab or VirtualKey.Enter) { draft.ReplaceSelection(key == VirtualKey.Tab ? "\t" : "\n"); preferredCaretX = null; SyncRichInput(); return; }
            var target = draft.Caret;
            if (key is VirtualKey.Left or VirtualKey.Right)
            {
                target = !shiftDown && draft.SelectionLength > 0 ? key == VirtualKey.Left ? draft.SelectionStart : draft.SelectionStart + draft.SelectionLength
                    : draft.Adjacent(target, key == VirtualKey.Left ? -1 : 1, ctrlDown);
                preferredCaretX = null;
            }
            else if (key is VirtualKey.Home or VirtualKey.End)
            {
                target = ctrlDown ? key == VirtualKey.Home ? 0 : draft.Block.Text.Length : Renderer.TextLineEdge(draft.Block, target, key == VirtualKey.End);
                preferredCaretX = null;
            }
            else
            {
                var rect = Renderer.CaretBounds(draft.Block, target); preferredCaretX ??= rect.X;
                var distance = key is VirtualKey.PageUp or VirtualKey.PageDown ? Math.Max(1, (int)(ActualHeight / (rect.Height * Zoom))) : 1;
                target = Renderer.MoveTextLine(draft.Block, target, (key is VirtualKey.Up or VirtualKey.PageUp ? -1 : 1) * distance, preferredCaretX.Value);
            }
            SetRichSelection(shiftDown ? draft.Anchor : target, target);
        });
        DispatcherQueue.TryEnqueue(DrainRichKeys);
        return true;
    }
    public void CopyTextFormat() { FlushPendingText(); copiedFormat = CurrentTextFormat; SelectionChanged?.Invoke(this, EventArgs.Empty); }
    public void PasteTextFormat() { if (copiedFormat is { } format) FormatSelection(target => RichText.CopyStyle(format, target)); }
    public void ClearTextFormatting() => FormatSelection(target => {
        var defaults = new TextFormat { Alignment = target.Alignment, Bullets = target.Bullets, Numbered = target.Numbered };
        RichText.CopyStyle(defaults, target);
    });
    public void FormatParagraph(Action<TextFormat> apply)
    {
        if (SelectedBlock is null && Page is not null) NewText();
        FlushPendingText(); if (HasPendingText) return;
        if (richDraft is not null) { richDraft.FormatContainer(apply); FlushPendingText(); UpdateRichAdorners(); }
        else if (SelectedBlock is { Kind: BlockKind.Text or BlockKind.Heading or BlockKind.Checklist } block && session is not null && Page is not null)
            session.EditPage(Page.Id, "Paragraph format", p => { var b = p.Blocks.First(b => b.Id == block.Id); RichText.Apply(b, 0, 0, apply); b.Height = Math.Clamp(Renderer.MeasureHeight(b), 40, 20000); });
        FocusTextEditor(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetTextFlow(TextFlowSettings flow)
    {
        flow.Validate(); FlushPendingText(); if (HasPendingText) return;
        if (richDraft is not null) { richDraft.SetFlow(flow); UpdateRichAdorners(); FlushPendingText(); }
        else if (SelectedBlock is { Kind: BlockKind.Text or BlockKind.Heading or BlockKind.Checklist } block && session is not null && Page is not null)
            session.EditPage(Page.Id, "Text layout", p => { var b = p.Blocks.First(b => b.Id == block.Id); b.TextFlow = flow.Copy(); b.Height = Math.Clamp(Renderer.MeasureHeight(b), 40, 20000); });
        FocusTextEditor(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
