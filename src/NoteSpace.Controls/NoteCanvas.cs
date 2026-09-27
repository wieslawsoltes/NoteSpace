using NoteSpace.Core;
using NoteSpace.Rendering.Skia;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace NoteSpace.Controls;

public sealed class NoteCanvas : SKCanvasElement
{
    private readonly PageRenderer renderer = new();
    public NotePage? Page { get; set; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (Page is not null) renderer.Render(canvas, Page, (float)area.Width, (float)area.Height);
    }
}
