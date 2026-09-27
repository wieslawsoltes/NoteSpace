using System.Globalization;
using System.Text.RegularExpressions;
using NoteSpace.Core;
using NoteSpace.Editor;
using SkiaSharp;

namespace NoteSpace.Rendering.Skia;

public sealed class RenderOptions
{
    public float Zoom { get; set; } = 1;
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public bool Dark { get; set; }
    public bool EditingTitle { get; set; }
    public string? SelectedId { get; set; }
    public string? HoverId { get; set; }
    public string? EditingId { get; set; }
    public NoteBlock? PreviewBlock { get; set; }
    public InkStroke? PreviewInk { get; set; }
    public ISet<string>? HiddenInk { get; set; }
}
public sealed record TextFragment(string Text, float X, float Baseline, float Width, TextFormat Format);
public sealed record TextLayout(IReadOnlyList<TextFragment> Fragments, float Height);

/// <summary>Viewport renderer without an Uno dependency. Instances own their native caches.</summary>
public sealed class PageRenderer : IDisposable
{
    private readonly Dictionary<(string Family, bool Bold, bool Italic), SKTypeface> faces = new();
    private readonly Dictionary<string, (int Key, TextLayout Value)> layouts = new();
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
        var key = (family, bold, italic);
        if (faces.Remove(key, out var old) && !ReferenceEquals(old, SKTypeface.Default)) old.Dispose();
        faces[key] = face;
        layouts.Clear();
    }
    private SKTypeface Face(TextFormat format)
    {
        var key = (format.FontFamily, format.Bold, format.Italic);
        if (faces.TryGetValue(key, out var face)) return face;
        face = SKTypeface.FromFamilyName(format.FontFamily, format.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, format.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default;
        faces.Add(key, face); return face;
    }
    private float Measure(string text, TextFormat format) { using var font = new SKFont(Face(format), format.FontSize); return font.MeasureText(text); }
    public TextLayout Layout(NoteBlock b)
    {
        var hash = new HashCode(); hash.Add(b.Text); hash.Add(b.Width); hash.Add(b.Kind); AddFormat(ref hash, b.Format);
        foreach (var m in b.Marks) { hash.Add(m.Start); hash.Add(m.Length); AddFormat(ref hash, m.Format); }
        var key = hash.ToHashCode();
        if (layouts.TryGetValue(b.Id, out var cached) && cached.Key == key) return cached.Value;
        var fragments = new List<TextFragment>();
        var indent = b.Kind == BlockKind.Checklist || b.Format.Bullets || b.Format.Numbered ? 24f : 0f;
        var width = Math.Max(12, b.Width - 24 - indent);
        var x = 0f; var y = 0f; var lineHeight = b.Format.FontSize * 1.45f;
        var line = new List<(string Text, float X, float Width, TextFormat Format)>();
        var lineNumber = 1;
        void Flush()
        {
            var shift = b.Format.Alignment == 1 ? Math.Max(0, (width - x) / 2) : b.Format.Alignment == 2 ? Math.Max(0, width - x) : 0;
            if (b.Format.Bullets || b.Format.Numbered)
                fragments.Add(new TextFragment(b.Format.Numbered ? (lineNumber++).ToString() + "." : "•", 12, y + lineHeight - 5, 20, b.Format));
            foreach (var f in line) fragments.Add(new TextFragment(f.Text, 12 + indent + f.X + shift, y + lineHeight - 5, f.Width, f.Format));
            y += lineHeight; x = 0; line.Clear(); lineHeight = b.Format.FontSize * 1.45f;
        }
        void Piece(string text, TextFormat format)
        {
            var measured = Measure(text, format);
            if (x > 0 && x + measured > width && !string.IsNullOrWhiteSpace(text)) Flush();
            if (measured <= width)
            {
                line.Add((text, x, measured, format)); x += measured; lineHeight = Math.Max(lineHeight, format.FontSize * 1.45f); return;
            }
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var value = elements.GetTextElement(); var advance = Measure(value, format);
                if (x > 0 && x + advance > width) Flush();
                line.Add((value, x, advance, format)); x += advance; lineHeight = Math.Max(lineHeight, format.FontSize * 1.45f);
            }
        }
        foreach (Match token in Regex.Matches(b.Text, @"\r\n|\r|\n|[^\S\r\n]+|[^\s]+"))
        {
            if (token.Value is "\n" or "\r" or "\r\n") { Flush(); continue; }
            var boundaries = new SortedSet<int> { token.Index, token.Index + token.Length };
            foreach (var mark in b.Marks)
            {
                if (mark.Start > token.Index && mark.Start < token.Index + token.Length) boundaries.Add(mark.Start);
                var end = mark.Start + mark.Length; if (end > token.Index && end < token.Index + token.Length) boundaries.Add(end);
            }
            var positions = boundaries.ToArray();
            for (var i = 0; i < positions.Length - 1; i++) Piece(b.Text[positions[i]..positions[i + 1]], RichText.At(b, positions[i]));
        }
        Flush();
        var result = new TextLayout(fragments, y + 24);
        if (layouts.Count > 2000) layouts.Clear();
        layouts[b.Id] = (key, result); return result;
    }
    private static void AddFormat(ref HashCode hash, TextFormat f)
    {
        hash.Add(f.FontFamily); hash.Add(f.FontSize); hash.Add(f.Bold); hash.Add(f.Italic); hash.Add(f.Underline); hash.Add(f.Strike); hash.Add(f.Color); hash.Add(f.Highlight); hash.Add(f.Alignment); hash.Add(f.Bullets); hash.Add(f.Numbered);
    }
    public float MeasureHeight(NoteBlock block) => block.Kind == BlockKind.Table ? Math.Max(42, block.Cells.Count * 42) : Math.Max(40, Layout(block).Height);
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
        var o = options ?? new RenderOptions();
        var background = o.Dark && page.PaperColor == 0xFFFFFFFF ? new SKColor(0xFF202020) : new SKColor(page.PaperColor);
        canvas.Clear(background);
        canvas.Save(); canvas.Scale(o.Zoom); canvas.Translate(-o.OffsetX, -o.OffsetY);
        var viewport = new NoteRect(o.OffsetX, o.OffsetY, width / o.Zoom, height / o.Zoom);
        DrawPaper(canvas, page.Paper, viewport, o.Dark);
        if (viewport.Y < 118)
        {
            if (!o.EditingTitle) Text(canvas, page.Title, 48, 66, new TextFormat { FontSize = 32, Color = o.Dark ? 0xFFF3F3F3 : 0xFF242424 });
            using var line = new SKPaint { Color = new SKColor(o.Dark ? 0xFF555555 : 0xFFCECECE), StrokeWidth = 1 };
            canvas.DrawLine(48, 83, 690, 83, line);
            Text(canvas, page.Created.ToString("dddd, MMMM d, yyyy     h:mm tt", CultureInfo.InvariantCulture), 48, 105, new TextFormat { FontSize = 12, Color = o.Dark ? 0xFFBBBBBB : 0xFF767676 });
        }
        foreach (var original in page.Blocks)
        {
            var b = o.PreviewBlock?.Id == original.Id ? o.PreviewBlock : original;
            if (!b.Bounds.Intersects(viewport)) continue;
            if (o.EditingId != b.Id) DrawBlock(canvas, b, o.Dark);
            if (b.Id == o.SelectedId || b.Id == o.HoverId) DrawContainer(canvas, b, b.Id == o.SelectedId);
        }
        foreach (var ink in page.Ink) if (o.HiddenInk?.Contains(ink.Id) != true && InkGeometry.Bounds(ink).Intersects(viewport)) DrawInk(canvas, ink);
        if (o.PreviewInk is not null) DrawInk(canvas, o.PreviewInk);
        canvas.Restore();
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
    private void DrawBlock(SKCanvas canvas, NoteBlock b, bool dark)
    {
        canvas.Save(); canvas.ClipRect(SKRect.Create(b.X, b.Y, b.Width, b.Height));
        if (b.Kind == BlockKind.Divider)
        {
            using var p = new SKPaint { Color = new SKColor(0xFFB6A3C6), StrokeWidth = 1.5f }; canvas.DrawLine(b.X, b.Y + 10, b.X + b.Width, b.Y + 10, p);
        }
        else if (b.Kind == BlockKind.Table) DrawTable(canvas, b, dark);
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
            foreach (var run in Layout(b).Fragments)
            {
                var f = run.Format;
                var color = dark && (f.Color & 0x00FFFFFF) < 0x00404040 ? 0xFFF0F0F0 : f.Color;
                using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(color) };
                var x = b.X + run.X; var baseline = b.Y + 12 + run.Baseline;
                if (f.Highlight != 0) { paint.Color = new SKColor(f.Highlight); canvas.DrawRect(x, baseline - f.FontSize, run.Width, f.FontSize * 1.3f, paint); paint.Color = new SKColor(color); }
                using var font = new SKFont(Face(f), f.FontSize);
                canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, font, paint);
                paint.StrokeWidth = Math.Max(1, f.FontSize / 16);
                if (f.Underline) canvas.DrawLine(x, baseline + 2, x + run.Width, baseline + 2, paint);
                if (f.Strike || b.Checked) canvas.DrawLine(x, baseline - f.FontSize * 0.3f, x + run.Width, baseline - f.FontSize * 0.3f, paint);
            }
        }
        canvas.Restore();
        if (b.Tags.Count > 0) Text(canvas, string.Join("  ", b.Tags.Select(t => t switch { "Important" => "★", "Question" => "?", "Idea" => "◇", _ => "⚑" })), b.X + b.Width + 8, b.Y + 25, new TextFormat { FontSize = 17, Color = 0xFF9861B9 });
    }
    private void DrawTable(SKCanvas canvas, NoteBlock b, bool dark)
    {
        var count = Math.Max(1, b.Cells.Select(r => r.Count).DefaultIfEmpty(1).Max());
        var cw = b.Width / count; var rh = 42f;
        using var paint = new SKPaint { IsAntialias = true };
        for (var row = 0; row < b.Cells.Count; row++)
        {
            for (var col = 0; col < count; col++)
            {
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
        using var font = new SKFont(Face(format), format.FontSize); using var paint = new SKPaint { Color = new SKColor(format.Color), IsAntialias = true };
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
    public void ClearCaches() { layouts.Clear(); foreach (var i in images.Values) i.Bitmap.Dispose(); images.Clear(); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; ClearCaches();
        foreach (var face in faces.Values) if (!ReferenceEquals(face, SKTypeface.Default)) face.Dispose(); faces.Clear();
    }
}
