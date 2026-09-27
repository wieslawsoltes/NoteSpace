using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace NoteSpace.Controls;

/// <summary>Original, resolution-independent line icons; no Microsoft artwork is bundled.</summary>
public sealed class NoteIcon : SKCanvasElement
{
    public string Glyph { get; set; } = "page";
    public uint InkColor { get; set; } = 0xFF3C3C3C;
    private static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>
    {
        ["book"] = "M5 3H19V21H5Q3 21 3 19V5Q3 3 5 3M7 3V21M11 7H16M11 10H16",
        ["page"] = "M6 2H15L20 7V22H6ZM15 2V7H20M9 11H17M9 15H17M9 18H14",
        ["add"] = "M12 5V19M5 12H19",
        ["menu"] = "M4 6H20M4 12H20M4 18H20",
        ["search"] = "M15 15L21 21M18 10A8 8 0 1 1 2 10A8 8 0 1 1 18 10",
        ["save"] = "M4 3H18L21 6V21H3V3ZM7 3V9H17V3M7 21V14H17V21M14 5V7",
        ["undo"] = "M9 5L3 10L9 15M4 10H14Q21 10 21 17V20",
        ["redo"] = "M15 5L21 10L15 15M20 10H10Q3 10 3 17V20",
        ["clipboard"] = "M8 5H4V22H20V5H16M9 2H15V7H9ZM8 11H16M8 15H16M8 18H13",
        ["copy"] = "M8 7H21V22H8ZM4 17H2V2H15V4",
        ["cut"] = "M9 8L21 21M9 16L21 3M10 6A4 4 0 1 1 2 6A4 4 0 1 1 10 6M10 18A4 4 0 1 1 2 18A4 4 0 1 1 10 18",
        ["pen"] = "M4 17L17 3L21 7L8 21L3 22ZM14 6L18 10M4 17L8 21",
        ["highlighter"] = "M8 15L16 3L22 7L14 19ZM8 15L14 19L10 22H5L3 20ZM3 23H22",
        ["eraser"] = "M3 15L14 3L22 11L11 22H8ZM8 10L16 18M11 22H22",
        ["select"] = "M5 2L21 13L13 15L10 23ZM12 15L17 22",
        ["text"] = "M3 4H21M12 4V21M8 21H16M3 4V8M21 4V8",
        ["rectangle"] = "M3 5H21V19H3Z",
        ["ellipse"] = "M22 12A10 8 0 1 1 2 12A10 8 0 1 1 22 12",
        ["line"] = "M3 21L21 3",
        ["link"] = "M9 8L12 5Q17 0 21 4Q25 8 19 13L16 16M8 9L5 12Q0 17 4 21Q8 25 13 19L16 16M8 16L16 8",
        ["image"] = "M3 3H21V21H3ZM3 17L8 11L12 15L16 10L21 17M9 7A2 2 0 1 1 5 7A2 2 0 1 1 9 7",
        ["table"] = "M2 4H22V21H2ZM2 9H22M2 15H22M9 4V21M16 4V21",
        ["attachment"] = "M8 15L16 7Q18 5 20 7Q22 9 20 11L10 21Q7 24 3 20Q0 17 3 14L15 2Q18 -1 22 3",
        ["settings"] = "M12 3V6M12 18V21M3 12H6M18 12H21M5 5L7 7M17 17L19 19M5 19L7 17M17 7L19 5M18 12A6 6 0 1 1 6 12A6 6 0 1 1 18 12",
        ["grip"] = "M8 5H9M15 5H16M8 12H9M15 12H16M8 19H9M15 19H16",
        ["chevron-right"] = "M9 7L14 12L9 17",
        ["chevron"] = "M7 9L12 14L17 9",
        ["close"] = "M5 5L19 19M19 5L5 19",
        ["tag"] = "M3 3H12L22 13L13 22L3 12ZM9 7A2 2 0 1 1 5 7A2 2 0 1 1 9 7",
        ["star"] = "M12 2L15 9L23 10L17 15L19 23L12 19L5 23L7 15L1 10L9 9Z",
        ["check"] = "M3 3H21V21H3ZM7 12L11 16L18 8",
        ["bullets"] = "M8 5H21M8 12H21M8 19H21M3 5H4M3 12H4M3 19H4",
        ["numbered"] = "M8 5H21M8 12H21M8 19H21M2 3H3V7M2 10H4L2 14H4M2 17H4V21H2M3 19H4",
        ["align-left"] = "M3 4H21M3 9H15M3 14H21M3 19H15",
        ["align-center"] = "M3 4H21M6 9H18M3 14H21M6 19H18",
        ["align-right"] = "M3 4H21M9 9H21M3 14H21M9 19H21",
        ["download"] = "M12 2V16M6 10L12 16L18 10M3 16V22H21V16",
        ["upload"] = "M12 17V3M6 9L12 3L18 9M3 16V22H21V16",
        ["history"] = "M3 4V10H9M3 10Q4 2 13 2Q22 2 22 12Q22 22 12 22Q5 22 3 17M12 6V12L16 15",
        ["trash"] = "M3 6H21M6 6L7 22H17L18 6M8 6V2H16V6M10 10V18M14 10V18",
        ["grid"] = "M3 3H21V21H3ZM3 9H21M3 15H21M9 3V21M15 3V21",
        ["ruled"] = "M3 5H21M3 10H21M3 15H21M3 20H21M7 2V23",
        ["fullscreen"] = "M3 9V3H9M15 3H21V9M21 15V21H15M9 21H3V15",
        ["folder"] = "M2 6V21H22V7H12L10 3H2Z",
        ["color"] = "M12 2Q21 12 21 16A9 8 0 0 1 3 16Q3 12 12 2",
        ["calendar"] = "M3 5H21V22H3ZM7 2V8M17 2V8M3 11H21M7 15H10M14 15H17M7 18H10",
        ["indent"] = "M11 4H22M11 10H22M11 16H22M11 22H22M2 7L7 12L2 17",
        ["outdent"] = "M11 4H22M11 10H22M11 16H22M11 22H22M7 7L2 12L7 17"
    };
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        canvas.Save(); canvas.Scale((float)area.Width / 24, (float)area.Height / 24);
        using var paint = new SKPaint { Color = new SKColor(InkColor), IsAntialias = true, StrokeWidth = 1.45f, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        if (Paths.TryGetValue(Glyph, out var data)) { using var path = SKPath.ParseSvgPathData(data); canvas.DrawPath(path, paint); }
        else
        {
            paint.Style = SKPaintStyle.Fill;
            using var face = SKTypeface.FromFamilyName("Arial", Glyph == "B" ? SKFontStyle.Bold : Glyph == "I" ? SKFontStyle.Italic : SKFontStyle.Normal);
            using var font = new SKFont(face, 21);
            canvas.DrawText(Glyph.Length > 2 ? Glyph[..1] : Glyph, 12, 20, SKTextAlign.Center, font, paint);
            if (Glyph == "U") canvas.DrawLine(5, 22, 19, 22, paint);
        }
        canvas.Restore();
    }
}
