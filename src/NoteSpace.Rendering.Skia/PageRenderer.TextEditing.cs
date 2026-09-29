using System.Globalization;
using NoteSpace.Core;
using SkiaSharp;

namespace NoteSpace.Rendering.Skia;

/// <summary>Transient editing adorners, expressed in container-local coordinates.
/// The very same text layout is used for committed pages, drafts and caret hit testing.</summary>
public sealed class TextEditVisual(NoteBlock block)
{
    public NoteBlock Block { get; } = block;
    public IReadOnlyList<NoteRect> Selection { get; set; } = [];
    public NoteRect Caret { get; set; }
    public bool CaretVisible { get; set; } = true;
}
public sealed partial class PageRenderer
{
    private static int LineForOffset(TextLayout layout, int offset)
    {
        var low = 0; var high = layout.Lines.Count;
        while (low < high) { var mid = low + (high - low) / 2; if (layout.Lines[mid].Start <= offset) low = mid + 1; else high = mid; }
        return Math.Max(0, low - 1);
    }
    private float TextX(TextLayout layout, TextLine line, int offset)
    {
        var x = line.Left;
        for (var i = line.First; i < line.First + line.Count; i++)
        {
            var f = layout.Fragments[i]; if (f.SourceStart < 0) continue;
            if (offset <= f.SourceStart) return f.X;
            if (offset < f.SourceStart + f.Text.Length)
                return f.X + (f.IsTab ? 0 : Font(f.Format).MeasureText(f.Text[..(offset - f.SourceStart)]));
            x = f.X + f.Width;
        }
        return x;
    }
    public NoteRect CaretBounds(NoteBlock block, int offset)
    {
        var layout = Layout(block); offset = Math.Clamp(offset, 0, block.Text.Length);
        var line = layout.Lines[LineForOffset(layout, offset)];
        return new(TextX(layout, line, offset), 12 + line.Top, 1, Math.Max(1, line.Bottom - line.Top));
    }
    public IReadOnlyList<NoteRect> SelectionBounds(NoteBlock block, int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > block.Text.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (length == 0) return [];
        var layout = Layout(block); var end = start + length; var result = new List<NoteRect>();
        for (var i = LineForOffset(layout, start); i < layout.Lines.Count; i++)
        {
            var line = layout.Lines[i]; if (line.Start >= end) break;
            var left = TextX(layout, line, Math.Max(start, line.Start));
            var right = TextX(layout, line, Math.Min(end, line.End));
            if (end > line.End && line.NextStart > line.End) right += 8;
            result.Add(new(left, line.Top + 12, Math.Max(2, right - left), line.Bottom - line.Top));
        }
        return result;
    }
    /// <summary>Container-local hit test. Returns a complete text-element boundary.</summary>
    public int HitTestText(NoteBlock block, float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        var layout = Layout(block); var at = Math.Clamp(FirstVisibleLine(layout.Lines, y - 12), 0, layout.Lines.Count - 1);
        return HitLine(layout, layout.Lines[at], x);
    }
    private int HitLine(TextLayout layout, TextLine line, float x)
    {
        for (var i = line.First; i < line.First + line.Count; i++)
        {
            var f = layout.Fragments[i]; if (f.SourceStart < 0) continue;
            if (x <= f.X) return f.SourceStart;
            if (x > f.X + f.Width) continue;
            if (f.IsTab) return f.SourceStart + (x >= f.X + f.Width / 2 ? 1 : 0);
            var elements = StringInfo.ParseCombiningCharacters(f.Text);
            var low = 0; var high = elements.Length;
            while (low < high)
            {
                var mid = low + (high - low) / 2; var next = mid + 1 < elements.Length ? elements[mid + 1] : f.Text.Length;
                var a = Font(f.Format).MeasureText(f.Text[..elements[mid]]);
                var b = Font(f.Format).MeasureText(f.Text[..next]);
                if (x - f.X < (a + b) / 2) high = mid; else low = mid + 1;
            }
            return f.SourceStart + (low == elements.Length ? f.Text.Length : elements[low]);
        }
        return line.End;
    }
    public int MoveTextLine(NoteBlock block, int offset, int direction, float preferredX)
    {
        var layout = Layout(block); var line = Math.Clamp(LineForOffset(layout, offset) + direction, 0, layout.Lines.Count - 1);
        return HitLine(layout, layout.Lines[line], preferredX);
    }
    public int TextLineEdge(NoteBlock block, int offset, bool end)
    {
        var layout = Layout(block); var line = layout.Lines[LineForOffset(layout, Math.Clamp(offset, 0, block.Text.Length))];
        return end ? line.End : line.Start;
    }
    private static void DrawTextEdit(SKCanvas canvas, TextEditVisual edit, RenderOptions options)
    {
        var b = edit.Block; canvas.Save(); canvas.ClipRect(SKRect.Create(b.X, b.Y, b.Width, b.Height));
        using var paint = new SKPaint { IsAntialias = false, Color = new SKColor(options.Dark ? 0x777E9FE6 : 0x554C82CC) };
        foreach (var rect in edit.Selection) canvas.DrawRect(SKRect.Create(b.X + rect.X, b.Y + rect.Y, rect.Width, rect.Height), paint);
        if (edit.CaretVisible && edit.Selection.Count == 0)
        {
            paint.Color = new SKColor(options.Dark ? 0xFFF2F2F2 : 0xFF242424);
            var rect = edit.Caret; canvas.DrawRect(SKRect.Create(b.X + rect.X, b.Y + rect.Y, 1.25f / options.Zoom, rect.Height), paint);
        }
        canvas.Restore();
    }
}
