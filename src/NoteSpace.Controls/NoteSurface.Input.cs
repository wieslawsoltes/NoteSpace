using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NoteSpace.Core;
using NoteSpace.Editor;
using Windows.Foundation;
using Windows.System;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface
{
    private uint? activePointer;
    private InkPoint startPoint;
    private Point startScreen;
    private float startOffsetX, startOffsetY;
    private InkStroke? drawing;
    private NoteBlock? moving;
    private readonly HashSet<string> erased = [];
    private int gesture; // 1 ink, 2 eraser, 3 move, 4 resize, 5 pan
    private bool shift, control, space;
    private InkPoint World(Point p, float pressure = 0.5f) => new((float)p.X / Zoom + canvas.Options.OffsetX, (float)p.Y / Zoom + canvas.Options.OffsetY, Math.Clamp(pressure, 0, 1));
    private readonly List<int> hitCandidates = [];
    private NoteBlock? Hit(InkPoint p)
    {
        if (Page is not { } page) return null;
        Renderer.ContentIndex(page, session?.Document.Revision).QueryBlocks(new(p.X - 3, p.Y - 3, 6, 6), hitCandidates);
        for (var i = hitCandidates.Count - 1; i >= 0; i--)
        {
            var b = page.Blocks[hitCandidates[i]];
            if (new NoteRect(b.X, b.Y - 12, b.Width, b.Height + 12).Contains(p.X, p.Y, 3)) return b;
        }
        return null;
    }
    private void ConfigureInput()
    {
        canvas.PointerPressed += PointerDown; canvas.PointerMoved += PointerMove; canvas.PointerReleased += PointerUp;
        canvas.PointerCanceled += (_, _) => CancelGesture(); canvas.PointerCaptureLost += (_, _) => CancelGesture();
        canvas.DoubleTapped += (_, e) => {
            if (Tool is not (DrawingTool.Select or DrawingTool.Text) || Page is null) return;
            var p = World(e.GetPosition(canvas));
            if (richDraft is not null && richDraft.Block.Bounds.Contains(p.X, p.Y))
            {
                var b = richDraft.Block; richDraft.SelectWord(Renderer.HitTestText(b, p.X - b.X, p.Y - b.Y));
                SetRichSelection(richDraft.Anchor, richDraft.Caret); editor?.Focus(FocusState.Programmatic); e.Handled = true; return;
            }
            if (p.Y < 95) BeginEditTitle(); else if (Hit(p) is { } block) { if (NoteTable.HitTest(block, p.X, p.Y) is { } cell) BeginEditTableCell(block, cell); else BeginEdit(block); } else if (p.Y >= 120) NewText(p.X, p.Y);
            e.Handled = true;
        };
        canvas.PointerWheelChanged += (_, e) => {
            var p = e.GetCurrentPoint(canvas); var delta = p.Properties.MouseWheelDelta;
            if (control) SetZoom(Zoom * (delta > 0 ? 1.1f : 1 / 1.1f));
            else if (shift || p.Properties.IsHorizontalMouseWheel) canvas.Options.OffsetX = Math.Clamp(canvas.Options.OffsetX - delta * 0.55f / Zoom, 0, 100000);
            else canvas.Options.OffsetY = Math.Clamp(canvas.Options.OffsetY - delta * 0.55f / Zoom, 0, 100000);
            Refresh(); e.Handled = true;
        };
        canvas.PointerExited += (_, _) => { if (activePointer is null) { canvas.Options.HoverId = null; canvas.Invalidate(); } };
        KeyDown += (_, e) => {
            if (e.Key == VirtualKey.Shift) shift = true;
            if (e.Key == VirtualKey.Control) control = true;
            if (e.Key == VirtualKey.Space && editor is null) space = true;
            if (editor is not null) return;
            if (e.Key == VirtualKey.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.Key == VirtualKey.Enter && SelectedBlock is { } b) { BeginEdit(b); e.Handled = true; }
            else if (e.Key == VirtualKey.Escape) { CancelGesture(); SelectBlock(null); e.Handled = true; }
        };
        KeyUp += (_, e) => { if (e.Key == VirtualKey.Shift) shift = false; if (e.Key == VirtualKey.Control) control = false; if (e.Key == VirtualKey.Space) space = false; };
        LostFocus += (_, _) => { shift = false; control = false; space = false; };
    }
    private void PointerDown(object sender, PointerRoutedEventArgs e)
    {
        if (Page is null || session is null || activePointer is not null) return;
        var current = e.GetCurrentPoint(canvas); var p = World(current.Position, current.Properties.Pressure);
        if (current.Properties.IsRightButtonPressed) { SelectBlock(Hit(p)?.Id); e.Handled = true; return; }
        if (richDraft is { } draft && (Tool is DrawingTool.Select or DrawingTool.Text)
            && !current.Properties.IsMiddleButtonPressed && !space
            && draft.Block.Bounds.Contains(p.X, p.Y) && p.Y >= draft.Block.Y + 4
            && !(p.X > draft.Block.X + draft.Block.Width - 14 && p.Y > draft.Block.Y + draft.Block.Height - 14))
        {
            var at = Renderer.HitTestText(draft.Block, p.X - draft.Block.X, p.Y - draft.Block.Y);
            SetRichSelection(shift ? draft.Anchor : at, at); editor?.Focus(FocusState.Programmatic);
            gesture = 6; activePointer = e.Pointer.PointerId; canvas.CapturePointer(e.Pointer); e.Handled = true; return;
        }
        EndEditing(); if (pendingText) return;
        if (current.Properties.IsMiddleButtonPressed || space)
        {
            gesture = 5; startOffsetX = canvas.Options.OffsetX; startOffsetY = canvas.Options.OffsetY; startScreen = current.Position;
        }
        else if (Tool is DrawingTool.Pen or DrawingTool.Highlighter or DrawingTool.Rectangle or DrawingTool.Ellipse or DrawingTool.Line)
        {
            gesture = 1; drawing = new InkStroke { Tool = Tool, Color = PenColor, Width = Tool == DrawingTool.Highlighter ? Math.Max(12, PenWidth * 4) : PenWidth, Points = [p] };
            canvas.Options.PreviewInk = drawing;
        }
        else if (Tool == DrawingTool.Eraser)
        {
            gesture = 2; erased.Clear(); EraseAt(p); canvas.Options.HiddenInk = erased;
        }
        else
        {
            var block = Hit(p); SelectBlock(block?.Id);
            if (block is not null && NoteTable.HitTest(block, p.X, p.Y) is { } cell) { tableCell = cell; Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
            if (block is not null)
            {
                if (block.Kind == BlockKind.Checklist && p.X < block.X + 33 && p.Y >= block.Y + 5 && p.Y <= block.Y + 36)
                {
                    session.EditPage(Page.Id, "Toggle to-do", page => { var b = page.Blocks.First(b => b.Id == block.Id); b.Checked = !b.Checked; }); e.Handled = true; return;
                }
                if (p.X > block.X + block.Width - 14 && p.Y > block.Y + block.Height - 14) gesture = 4;
                else if (p.Y < block.Y + 4) gesture = 3;
                else if (Tool == DrawingTool.Text) { BeginEdit(block); e.Handled = true; return; }
                if (gesture is 3 or 4) { moving = DocumentJson.CloneBlock(block); canvas.Options.PreviewBlock = DocumentJson.CloneBlock(block); }
            }
            else if (Tool == DrawingTool.Text && p.Y >= 120) { NewText(p.X, p.Y); e.Handled = true; return; }
        }
        if (gesture != 0) { startPoint = p; activePointer = e.Pointer.PointerId; canvas.CapturePointer(e.Pointer); e.Handled = true; }
        canvas.Invalidate();
    }
    private void PointerMove(object sender, PointerRoutedEventArgs e)
    {
        var current = e.GetCurrentPoint(canvas); var p = World(current.Position, current.Properties.Pressure);
        if (activePointer != e.Pointer.PointerId)
        {
            var hover = Hit(p)?.Id; if (hover != canvas.Options.HoverId) { canvas.Options.HoverId = hover; canvas.Invalidate(); } return;
        }
        if (gesture == 6 && richDraft is { } rich)
        {
            if (current.Position.Y < 0) canvas.Options.OffsetY = Math.Max(0, canvas.Options.OffsetY - 16 / Zoom);
            else if (current.Position.Y > ActualHeight) canvas.Options.OffsetY = Math.Min(100000, canvas.Options.OffsetY + 16 / Zoom);
            p = World(current.Position, current.Properties.Pressure);
            SetRichSelection(rich.Anchor, Renderer.HitTestText(rich.Block, p.X - rich.Block.X, p.Y - rich.Block.Y));
        }
        else if (gesture == 1 && drawing is not null)
        {
            if (drawing.Tool is DrawingTool.Rectangle or DrawingTool.Ellipse or DrawingTool.Line)
            {
                if (shift) { var size = Math.Max(Math.Abs(p.X - startPoint.X), Math.Abs(p.Y - startPoint.Y)); p = new InkPoint(startPoint.X + Math.Sign(p.X - startPoint.X) * size, startPoint.Y + Math.Sign(p.Y - startPoint.Y) * size, p.Pressure); }
                if (drawing.Points.Count == 1) drawing.Points.Add(p); else drawing.Points[1] = p;
            }
            else foreach (var point in e.GetIntermediatePoints(canvas).Reverse())
            {
                var next = World(point.Position, point.Properties.Pressure); var last = drawing.Points[^1];
                if (Math.Abs(next.X - last.X) + Math.Abs(next.Y - last.Y) >= 0.3f && drawing.Points.Count < 100000) drawing.Points.Add(next);
            }
        }
        else if (gesture == 2) EraseAt(p);
        else if (gesture is 3 or 4 && moving is not null && canvas.Options.PreviewBlock is { } preview)
        {
            var dx = p.X - startPoint.X; var dy = p.Y - startPoint.Y;
            if (gesture == 3) { preview.X = Math.Clamp(moving.X + dx, 0, 99000); preview.Y = Math.Clamp(moving.Y + dy, 120, 99000); }
            else { preview.Width = Math.Clamp(moving.Width + dx, 120, 20000); preview.Height = Math.Clamp(moving.Height + dy, 40, 20000); }
        }
        else if (gesture == 5)
        {
            canvas.Options.OffsetX = Math.Clamp(startOffsetX - (float)(current.Position.X - startScreen.X) / Zoom, 0, 100000);
            canvas.Options.OffsetY = Math.Clamp(startOffsetY - (float)(current.Position.Y - startScreen.Y) / Zoom, 0, 100000);
        }
        canvas.Invalidate(); e.Handled = true;
    }
    private void EraseAt(InkPoint p)
    {
        if (Page is null) return;
        var radius = 9 / Zoom;
        Renderer.ContentIndex(Page, session?.Document.Revision).QueryInk(new(p.X - radius, p.Y - radius, radius * 2, radius * 2), hitCandidates);
        foreach (var at in hitCandidates) { var stroke = Page.Ink[at]; if (!erased.Contains(stroke.Id) && InkGeometry.HitTest(stroke, p, radius)) erased.Add(stroke.Id); }
    }
    private void PointerUp(object sender, PointerRoutedEventArgs e)
    {
        if (activePointer != e.Pointer.PointerId || session is null || Page is null) return;
        var pageId = Page.Id;
        try
        {
            if (gesture == 1 && drawing is not null)
            {
                var stroke = drawing; if (stroke.Tool is DrawingTool.Pen or DrawingTool.Highlighter) stroke.Points = InkGeometry.Simplify(stroke.Points).ToList();
                session.EditPage(pageId, "Draw " + stroke.Tool.ToString().ToLowerInvariant(), p => p.Ink.Add(stroke));
            }
            else if (gesture == 2 && erased.Count > 0) session.EditPage(pageId, "Erase ink", p => p.Ink.RemoveAll(s => erased.Contains(s.Id)));
            else if (gesture is 3 or 4 && canvas.Options.PreviewBlock is { } preview)
            {
                var resize = gesture == 4;
                session.EditPage(pageId, resize ? "Resize note" : "Move note", p => {
                    var b = p.Blocks.First(b => b.Id == preview.Id); b.X = preview.X; b.Y = preview.Y; b.Width = preview.Width; b.Height = preview.Height;
                    if (resize && b.Kind is BlockKind.Text or BlockKind.Checklist or BlockKind.Heading) b.Height = Math.Max(b.Height, Renderer.MeasureHeight(b));
                });
            }
        }
        catch (Exception ex) { Error?.Invoke(this, ex.Message); }
        finally { activePointer = null; canvas.ReleasePointerCapture(e.Pointer); CancelGesture(); }
        Refresh(); e.Handled = true;
    }
    private void CancelGesture()
    {
        activePointer = null; gesture = 0; drawing = null; moving = null; erased.Clear();
        canvas.Options.PreviewInk = null; canvas.Options.PreviewBlock = null; canvas.Options.HiddenInk = null; canvas.Invalidate();
    }
}
