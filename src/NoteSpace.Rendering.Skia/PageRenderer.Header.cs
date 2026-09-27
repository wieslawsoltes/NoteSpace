using System.Globalization;
using NoteSpace.Core;
using SkiaSharp;

namespace NoteSpace.Rendering.Skia;

public sealed partial class PageRenderer
{
    private SKTextBlob? titleBlob, dateBlob;
    private string? headerTitle;
    private DateTimeOffset headerCreated;
    private bool headerValid;
    private readonly TextFormat titleStyle = new() { FontSize = 32 };
    private readonly TextFormat dateStyle = new() { FontSize = 12 };
    private readonly SKPaint headerPaint = new() { IsAntialias = true };
    private void ClearHeader()
    {
        titleBlob?.Dispose(); dateBlob?.Dispose(); titleBlob = null; dateBlob = null; headerTitle = null; headerValid = false;
    }
    private void DrawHeader(SKCanvas canvas, NotePage page, RenderOptions options)
    {
        if (!headerValid || headerTitle != page.Title || headerCreated != page.Created)
        {
            ClearHeader();
            titleBlob = SKTextBlob.Create(page.Title, Font(titleStyle));
            dateBlob = SKTextBlob.Create(page.Created.ToString("dddd, MMMM d, yyyy     h:mm tt", CultureInfo.InvariantCulture), Font(dateStyle));
            headerTitle = page.Title; headerCreated = page.Created; headerValid = true; Statistics.HeaderBuilds++;
        }
        if (!options.EditingTitle && titleBlob is not null)
        {
            headerPaint.Color = new SKColor(options.Dark ? 0xFFF3F3F3 : 0xFF242424);
            canvas.DrawText(titleBlob, 48, 66, headerPaint);
        }
        headerPaint.Color = new SKColor(options.Dark ? 0xFF555555 : 0xFFCECECE); headerPaint.StrokeWidth = 1;
        // The original separator used a non-antialiased paint.
        headerPaint.IsAntialias = false; canvas.DrawLine(48, 83, 690, 83, headerPaint); headerPaint.IsAntialias = true;
        if (dateBlob is not null)
        {
            headerPaint.Color = new SKColor(options.Dark ? 0xFFBBBBBB : 0xFF767676);
            canvas.DrawText(dateBlob, 48, 105, headerPaint);
        }
    }
}
