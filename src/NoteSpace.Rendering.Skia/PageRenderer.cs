using System.Globalization;
using System.Diagnostics;
using NoteSpace.Core;
using NoteSpace.Editor;
using SkiaSharp;

namespace NoteSpace.Rendering.Skia;

public sealed class RenderOptions
{
    /// <summary>Optional host content token. Advance after every page mutation. Without
    /// a token, spatial geometry is rebuilt on every render for mutable-DTO safety.</summary>
    public long? ContentRevision { get; set; }
    public float Zoom { get; set; } = 1;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool Dark { get; set; }
    public bool EditingTitle { get; set; }
    public string? SelectedId { get; set; }
    public TableCellAddress? SelectedCell { get; set; }
    public string? HoverId { get; set; }
    public string? EditingId { get; set; }
    public NoteBlock? PreviewBlock { get; set; }
    public InkStroke? PreviewInk { get; set; }
    public ISet<string>? HiddenInk { get; set; }
}
public sealed record TextFragment(string Text, float X, float Baseline, float Width, TextFormat Format);
public sealed record TextLayout(IReadOnlyList<TextFragment> Fragments, float Height)
{
    internal IReadOnlyList<TextLine> Lines { get; init; } = [];
}
internal readonly record struct TextLine(int First, int Count, float Top, float Bottom);

/// <summary>Viewport renderer without an Uno dependency. Instances own their native caches.</summary>
public sealed partial class PageRenderer : IDisposable
{
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> faces = new();
    private readonly Dictionary<string, (byte[] Data, SKBitmap Bitmap)> images = new();
    private bool disposed;
    /// <summary>Registers host-supplied font bytes for an exact family/style. This renderer owns the decoded face.</summary>
    public void RegisterTypeface(string family, bool bold, bool italic, byte[] fontData)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        ArgumentNullException.ThrowIfNull(fontData);
        if (fontData.Length > 32 * 1024 * 1024) throw new InvalidDataException("Font data exceeds 32 MiB.");
        using var data = SKData.CreateCopy(fontData);
        var face = SKTypeface.FromData(data) ?? throw new InvalidDataException("The supplied font could not be decoded.");
        ClearFonts();
        var key = (family, bold, italic);
        if (faces.Remove(key, out var old) && !ReferenceEquals(old, SKTypeface.Default)) old.Dispose();
        faces[key] = face;
        ClearLayouts();
    }
    private SKTypeface Face(TextFormat format)
    {
        var key = (format.FontFamily, format.Bold, format.Italic);
        if (faces.TryGetValue(key, out var face)) return face;
        face = SKTypeface.FromFamilyName(format.FontFamily, format.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, format.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        faces.Add(key, face); return face;
    }
    public float MeasureHeight(NoteBlock block) => block.Kind == BlockKind.Table ? Math.Max(42, block.Cells.Count * NoteTable.RowHeight) : Math.Max(40, Layout(block).Height);
    public static NoteRect Extent(NotePage page)
    {
        var right = 1100f; var bottom = 760f;
        foreach (var b in page.Blocks) { right = Math.Max(right, b.X + b.Width + 80); bottom = Math.Max(bottom, b.Y + b.Height + 120); }
        foreach (var s in page.Ink) { var b = InkGeometry.Bounds(s); right = Math.Max(right, b.X + b.Width + 80); bottom = Math.Max(bottom, b.Y + b.Height + 120); }
        return new(0, 0, right, bottom);
    }
    public void Render(SKCanvas canvas, NotePage page, float width, float height, RenderOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas); ArgumentNullException.ThrowIfNull(page);
        var o = options ?? new RenderOptions();
        if (!float.IsFinite(o.Zoom) || o.Zoom <= 0 || !float.IsFinite(o.OffsetX) || !float.IsFinite(o.OffsetY)
            || !float.IsFinite(width) || !float.IsFinite(height) || width < 0 || height < 0
            || !float.IsFinite(width / o.Zoom) || !float.IsFinite(height / o.Zoom)
            || Math.Abs(o.OffsetX) > 200000 || Math.Abs(o.OffsetY) > 200000 || width / o.Zoom > 200000 || height / o.Zoom > 200000) throw new ArgumentOutOfRangeException(nameof(options));
        var started = Stopwatch.GetTimestamp(); Statistics.BeginFrame();
        var background = o.Dark && page.PaperColor == 0xFFFFFFFF ? new SKColor(0xFF202020) : new SKColor(page.PaperColor);
        canvas.Clear(background);
        if (width == 0 || height == 0) return;
        var viewport = new NoteRect(o.OffsetX, o.OffsetY, width / o.Zoom, height / o.Zoom);
        var index = ContentIndex(page, o.ContentRevision);
        index.QueryBlocks(viewport, visibleBlocks); Statistics.CandidatesTested += index.LastCandidatesTested;
        index.QueryInk(viewport, visibleInk); Statistics.CandidatesTested += index.LastCandidatesTested;
        if (o.PreviewBlock is { } preview && preview.Bounds.Intersects(viewport))
        {
            var at = index.BlockIndex(preview.Id);
            if (at >= 0 && visibleBlocks.BinarySearch(at) < 0) { visibleBlocks.Add(at); visibleBlocks.Sort(); }
        }
        canvas.Save();
        try
        {
            canvas.Scale(o.Zoom); canvas.Translate(-o.OffsetX, -o.OffsetY);
            DrawPaper(canvas, page.Paper, viewport, o.Dark);
            if (viewport.Y < 118)
            {
                if (!o.EditingTitle) Text(canvas, page.Title, 48, 66, new TextFormat { FontSize = 32, Color = o.Dark ? 0xFFF3F3F3 : 0xFF242424 });
                using var line = new SKPaint { Color = new SKColor(o.Dark ? 0xFF555555 : 0xFFCECECE), StrokeWidth = 1 };
                canvas.DrawLine(48, 83, 690, 83, line);
                Text(canvas, page.Created.ToString("dddd, MMMM d, yyyy     h:mm tt", CultureInfo.InvariantCulture), 48, 105, new TextFormat { FontSize = 12, Color = o.Dark ? 0xFFBBBBBB : 0xFF767676 });
            }
            foreach (var at in visibleBlocks)
            {
                var original = page.Blocks[at]; var b = o.PreviewBlock?.Id == original.Id ? o.PreviewBlock : original;
                if (o.EditingId != b.Id) DrawBlock(canvas, b, o.Dark, viewport, ReferenceEquals(b, o.PreviewBlock) ? null : o.ContentRevision);
                if (b.Id == o.SelectedId || b.Id == o.HoverId) DrawContainer(canvas, b, b.Id == o.SelectedId);
                if (b.Id == o.SelectedId && b.Kind == BlockKind.Table && o.SelectedCell is { } cell && cell.Row < b.Cells.Count && cell.Column < NoteTable.ColumnCount(b) && cell.Row >= 0 && cell.Column >= 0)
                {
                    var bounds = NoteTable.CellBounds(b, cell);
                    using var selectedCell = new SKPaint { Color = new SKColor(0xFF803AB3), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
                    canvas.DrawRect(SKRect.Create(bounds.X, bounds.Y, bounds.Width, bounds.Height), selectedCell);
                }
                Statistics.BlocksDrawn++;
            }
            foreach (var at in visibleInk)
            {
                var stroke = page.Ink[at]; if (o.HiddenInk?.Contains(stroke.Id) == true) continue;
                if (o.ContentRevision.HasValue) DrawRetainedInk(canvas, stroke); else DrawInk(canvas, stroke);
                Statistics.InkDrawn++;
            }
            if (o.PreviewInk is not null) DrawInk(canvas, o.PreviewInk);
        }
        finally { canvas.Restore(); Statistics.RenderCpuMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
    }
    private static void DrawPaper(SKCanvas canvas, PaperStyle paper, NoteRect v, bool dark)
    {
        if (paper == PaperStyle.Plain) return;
        var step = paper == PaperStyle.Ruled ? 32f : 24f;
        using var paint = new SKPaint { Color = new SKColor(dark ? 0xFF373A40 : 0xFFE1EAF2), StrokeWidth = 0.8f, IsAntialias = true };
        var firstX = MathF.Floor(v.X / step) * step; var firstY = Math.Max(128, MathF.Floor(v.Y / step) * step);
        for (var y = firstY; y <= v.Y + v.Height; y += step)
        {
            if (paper == PaperStyle.Dots) for (var x = firstX; x <= v.X + v.Width; x += step) canvas.DrawCircle(x, y, 1, paint);
            else canvas.DrawLine(v.X, y, v.X + v.Width, y, paint);
        }
        if (paper == PaperStyle.Grid) for (var x = firstX; x <= v.X + v.Width; x += step) canvas.DrawLine(x, Math.Max(128, v.Y), x, v.Y + v.Height, paint);
    }
    private void DrawBlock(SKCanvas canvas, NoteBlock b, bool dark, NoteRect viewport, long? revision)
    {
        canvas.Save(); canvas.ClipRect(SKRect.Create(b.X, b.Y, b.Width, b.Height));
        if (b.Kind == BlockKind.Divider)
        {
            using var p = new SKPaint { Color = new SKColor(0xFFB6A3C6), StrokeWidth = 1.5f }; canvas.DrawLine(b.X, b.Y + 10, b.X + b.Width, b.Y + 10, p);
        }
        else if (b.Kind == BlockKind.Table) DrawTable(canvas, b, dark, viewport);
        else if (b.Kind == BlockKind.Image) DrawImage(canvas, b);
        else if (b.Kind == BlockKind.Attachment)
        {
            using var p = new SKPaint { IsAntialias = true, Color = new SKColor(dark ? 0xFF343038 : 0xFFF4F0F7) };
            canvas.DrawRoundRect(SKRect.Create(b.X, b.Y, b.Width, b.Height), 4, 4, p);
            Text(canvas, "↧", b.X + 15, b.Y + 37, new TextFormat { FontSize = 28, Color = 0xFF8E54B5 });
            Text(canvas, b.FileName, b.X + 52, b.Y + 28, new TextFormat { Bold = true, FontSize = 14, Color = dark ? 0xFFF2F2F2 : 0xFF242424 });
            Text(canvas, $"{(b.Data?.Length ?? 0) / 1024.0:0.#} KB · Double-click to save", b.X + 52, b.Y + 51, new TextFormat { FontSize = 12, Color = dark ? 0xFFBBBBBB : 0xFF707070 });
        }
        else
        {
            if (b.Kind == BlockKind.Checklist)
            {
                using var p = new SKPaint { IsAntialias = true, Color = new SKColor(b.Checked ? 0xFF803AB3 : dark ? 0xFFBFBFBF : 0xFF737373), Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
                var r = SKRect.Create(b.X + 12, b.Y + 16, 14, 14); canvas.DrawRoundRect(r, 1, 1, p);
                if (b.Checked) { p.StrokeWidth = 2; canvas.DrawLine(r.Left + 3, r.MidY, r.Left + 6, r.Bottom - 3, p); canvas.DrawLine(r.Left + 6, r.Bottom - 3, r.Right - 2, r.Top + 3, p); }
            }
            var layout = LayoutCore(b, revision);
            var top = viewport.Y - b.Y - 12; var bottom = top + viewport.Height;
            var first = FirstVisibleLine(layout.Lines, top);
            using var paint = new SKPaint { IsAntialias = true };
            for (var l = first; l < layout.Lines.Count && layout.Lines[l].Top <= bottom; l++)
            {
                var line = layout.Lines[l];
                for (var i = line.First; i < line.First + line.Count; i++)
                {
                    var run = layout.Fragments[i]; var f = run.Format;
                    var color = dark && (f.Color & 0x00FFFFFF) < 0x00404040 ? 0xFFF0F0F0 : f.Color;
                    paint.Color = new SKColor(color);
                    var x = b.X + run.X; var baseline = b.Y + 12 + run.Baseline;
                    if (f.Highlight != 0) { paint.Color = new SKColor(f.Highlight); canvas.DrawRect(x, baseline - f.FontSize, run.Width, f.FontSize * 1.3f, paint); paint.Color = new SKColor(color); }
                    canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, Font(f), paint);
                    paint.StrokeWidth = Math.Max(1, f.FontSize / 16);
                    if (f.Underline) canvas.DrawLine(x, baseline + 2, x + run.Width, baseline + 2, paint);
                    if (f.Strike || b.Checked) canvas.DrawLine(x, baseline - f.FontSize * 0.3f, x + run.Width, baseline - f.FontSize * 0.3f, paint);
                    Statistics.TextFragmentsDrawn++;
                }
            }
        }
        canvas.Restore();
        if (b.Tags.Count > 0) Text(canvas, string.Join("  ", b.Tags.Select(t => t switch { "Important" => "★", "Question" => "?", "Idea" => "◇", _ => "⚑" })), b.X + b.Width + 8, b.Y + 25, new TextFormat { FontSize = 17, Color = 0xFF9861B9 });
    }
    private void DrawTable(SKCanvas canvas, NoteBlock b, bool dark, NoteRect viewport)
    {
        var count = Math.Max(1, b.Cells.Select(r => r.Count).DefaultIfEmpty(1).Max());
        var cw = b.Width / count; var rh = NoteTable.RowHeight;
        var firstRow = Math.Clamp((int)MathF.Floor((viewport.Y - b.Y) / rh), 0, b.Cells.Count);
        var lastRow = Math.Min(b.Cells.Count, (int)MathF.Ceiling((viewport.Y + viewport.Height - b.Y) / rh));
        var firstColumn = Math.Clamp((int)MathF.Floor((viewport.X - b.X) / cw), 0, count);
        var lastColumn = Math.Min(count, (int)MathF.Ceiling((viewport.X + viewport.Width - b.X) / cw));
        using var paint = new SKPaint { IsAntialias = true };
        for (var row = firstRow; row < lastRow; row++)
        {
            for (var col = firstColumn; col < lastColumn; col++)
            {
                Statistics.TableCellsDrawn++;
                var rect = SKRect.Create(b.X + col * cw, b.Y + row * rh, cw, rh);
                paint.Style = SKPaintStyle.Fill; paint.Color = new SKColor(row == 0 ? dark ? 0xFF403448 : 0xFFF0E7F6 : dark ? 0xFF262626 : 0xFFFFFFFF); canvas.DrawRect(rect, paint);
                paint.Style = SKPaintStyle.Stroke; paint.Color = new SKColor(dark ? 0xFF615467 : 0xFFD7CCDE); paint.StrokeWidth = 1; canvas.DrawRect(rect, paint);
                canvas.Save(); canvas.ClipRect(SKRect.Create(rect.Left + 8, rect.Top, rect.Width - 16, rect.Height));
                Text(canvas, col < b.Cells[row].Count ? b.Cells[row][col].Replace('\n', ' ') : "", rect.Left + 10, rect.Top + 27, new TextFormat { FontSize = 14, Bold = row == 0, Color = dark ? 0xFFF0F0F0 : 0xFF353535 }); canvas.Restore();
            }
        }
    }
    private void DrawImage(SKCanvas canvas, NoteBlock b)
    {
        if (b.Data is null) return;
        SKBitmap? bitmap = null;
        if (images.TryGetValue(b.Id, out var existing) && ReferenceEquals(existing.Data, b.Data)) bitmap = existing.Bitmap;
        if (bitmap is null)
        {
            using var memory = new SKMemoryStream(b.Data); using var codec = SKCodec.Create(memory);
            if (codec is null || (long)codec.Info.Width * codec.Info.Height > 16 * 1024 * 1024) { Text(canvas, "Image is invalid or exceeds 16 megapixels", b.X + 12, b.Y + 30, new TextFormat { Color = 0xFFB03030 }); return; }
            bitmap = SKBitmap.Decode(b.Data); if (bitmap is null) return;
            if (images.Remove(b.Id, out var stale)) stale.Bitmap.Dispose();
            if (images.Count >= 24) { foreach (var image in images.Values) image.Bitmap.Dispose(); images.Clear(); }
            images[b.Id] = (b.Data, bitmap);
        }
        var scale = Math.Min(b.Width / bitmap.Width, b.Height / bitmap.Height);
        canvas.DrawBitmap(bitmap, SKRect.Create(b.X, b.Y, bitmap.Width * scale, bitmap.Height * scale));
    }
    private static void DrawContainer(SKCanvas canvas, NoteBlock b, bool selected)
    {
        using var p = new SKPaint { IsAntialias = true, Color = new SKColor(selected ? 0xFF9861B9 : 0xFFC5C5C5), StrokeWidth = 1, Style = SKPaintStyle.Stroke };
        canvas.DrawRect(SKRect.Create(b.X, b.Y, b.Width, b.Height), p);
        p.Style = SKPaintStyle.Fill; p.Color = new SKColor(selected ? 0xFFEFE5F6 : 0xFFF0F0F0); canvas.DrawRect(b.X, b.Y - 12, b.Width, 12, p);
        p.Color = new SKColor(0xFF9C8BA7);
        for (var i = -2; i <= 2; i++) canvas.DrawCircle(b.X + b.Width / 2 + i * 4, b.Y - 6, 0.9f, p);
        if (selected) { p.Color = new SKColor(0xFF9861B9); canvas.DrawRect(b.X + b.Width - 5, b.Y + b.Height - 5, 8, 8, p); }
    }
    public static void DrawInk(SKCanvas canvas, InkStroke stroke)
    {
        if (stroke.Points.Count == 0) return;
        using var paint = new SKPaint { Color = new SKColor(stroke.Color), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round, StrokeWidth = stroke.Width };
        if (stroke.Tool == DrawingTool.Highlighter) paint.Color = paint.Color.WithAlpha(90);
        var a = stroke.Points[0]; var z = stroke.Points[^1];
        var rect = new SKRect(Math.Min(a.X, z.X), Math.Min(a.Y, z.Y), Math.Max(a.X, z.X), Math.Max(a.Y, z.Y));
        if (stroke.Tool == DrawingTool.Rectangle) canvas.DrawRect(rect, paint);
        else if (stroke.Tool == DrawingTool.Ellipse) canvas.DrawOval(rect, paint);
        else if (stroke.Tool == DrawingTool.Line) canvas.DrawLine(a.X, a.Y, z.X, z.Y, paint);
        else if (stroke.Points.Count == 1) { paint.Style = SKPaintStyle.Fill; canvas.DrawCircle(a.X, a.Y, stroke.Width / 2, paint); }
        else if (stroke.Tool == DrawingTool.Highlighter)
        {
            using var path = new SKPath(); path.MoveTo(a.X, a.Y);
            foreach (var p in stroke.Points.Skip(1)) path.LineTo(p.X, p.Y);
            canvas.DrawPath(path, paint);
        }
        else for (var i = 1; i < stroke.Points.Count; i++)
        {
            a = stroke.Points[i - 1]; z = stroke.Points[i]; paint.StrokeWidth = stroke.Width * (0.5f + (a.Pressure + z.Pressure) / 2); canvas.DrawLine(a.X, a.Y, z.X, z.Y, paint);
        }
    }
    private void Text(SKCanvas canvas, string text, float x, float y, TextFormat format)
    {
        var font = Font(format); using var paint = new SKPaint { Color = new SKColor(format.Color), IsAntialias = true };
        canvas.DrawText(text, x, y, SKTextAlign.Left, font, paint);
    }
    public byte[] ExportPng(NotePage page, float scale = 1)
    {
        if (!float.IsFinite(scale) || scale is < 0.1f or > 4) throw new ArgumentOutOfRangeException(nameof(scale));
        var extent = Extent(page); var width = (int)Math.Ceiling(extent.Width * scale); var height = (int)Math.Ceiling(extent.Height * scale);
        if ((long)width * height > 16 * 1024 * 1024) throw new InvalidOperationException("This page is too large for a 16-megapixel PNG. Export HTML or reduce the scale.");
        using var surface = SKSurface.Create(new SKImageInfo(width, height)) ?? throw new InvalidOperationException("Could not allocate the export surface.");
        Render(surface.Canvas, page, width, height, new RenderOptions { Zoom = scale });
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 95); return data.ToArray();
    }
    public void ClearCaches()
    {
        ClearLayouts(); ClearInkPictures(); ClearFonts(); indexedPage = null; contentIndex = null;
        foreach (var i in images.Values) i.Bitmap.Dispose(); images.Clear();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; ClearCaches();
        foreach (var face in faces.Values) if (!ReferenceEquals(face, SKTypeface.Default)) face.Dispose(); faces.Clear();
    }
}
