using NoteSpace.Core;
using SkiaSharp;

namespace NoteSpace.Rendering.Skia;

public sealed class PageRenderer : IDisposable
{
    private readonly SKTypeface face = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;
    public void Render(SKCanvas canvas, NotePage page, float width, float height)
    {
        canvas.Clear(new SKColor(page.PaperColor));
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(0xFF242424) };
        using var title = new SKFont(face, 32);
        canvas.DrawText(page.Title, 48, 66, SKTextAlign.Left, title, paint);
        using var font = new SKFont(face, 16);
        foreach (var block in page.Blocks) { var y = block.Y + 22; foreach (var line in block.Text.Split('\n')) { canvas.DrawText(line, block.X, y, SKTextAlign.Left, font, paint); y += 24; } }
    }
    public void Dispose() => face.Dispose();
}
