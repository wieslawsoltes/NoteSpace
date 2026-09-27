using NoteSpace.Core;
using NoteSpace.Rendering.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace NoteSpace.Controls;

public sealed class NoteCanvas : SKCanvasElement, IDisposable
{
    public PageRenderer Renderer { get; } = new();
    public NotePage? Page { get; set; }
    public RenderOptions Options { get; } = new();
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (Page is not null) Renderer.Render(canvas, Page, (float)area.Width, (float)area.Height, Options);
        else canvas.Clear(new SKColor(Options.Dark ? 0xFF202020 : 0xFFFFFFFF));
    }
    public void Dispose() => Renderer.Dispose();
}
