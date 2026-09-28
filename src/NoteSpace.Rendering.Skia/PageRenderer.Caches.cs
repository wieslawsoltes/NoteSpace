using NoteSpace.Core;
using NoteSpace.Editor;
using SkiaSharp;

namespace NoteSpace.Rendering.Skia;

/// <summary>Cumulative cache counters and most-recent-frame work. CPU wall time is not GPU time or FPS.</summary>
public sealed class RendererStatistics
{
    public long HeaderBuilds { get; internal set; }
    public long ImageDecodeAttempts { get; internal set; }
    public long ImageCacheHits { get; internal set; }
    public long SpatialBuilds { get; internal set; }
    public long LayoutBuilds { get; internal set; }
    public long LayoutHits { get; internal set; }
    public long InkPictureBuilds { get; internal set; }
    public long FontBuilds { get; internal set; }
    public int CandidatesTested { get; internal set; }
    public int BlocksDrawn { get; internal set; }
    public int InkDrawn { get; internal set; }
    public int TableCellsDrawn { get; internal set; }
    public int TextFragmentsDrawn { get; internal set; }
    public double RenderCpuMilliseconds { get; internal set; }
    internal void BeginFrame() { CandidatesTested = 0; BlocksDrawn = 0; InkDrawn = 0; TextFragmentsDrawn = 0; TableCellsDrawn = 0; RenderCpuMilliseconds = 0; }
}

public sealed partial class PageRenderer
{
    private readonly List<int> visibleBlocks = [], visibleInk = [];
    private PageContentIndex? contentIndex;
    private NotePage? indexedPage;
    private long? indexedRevision;
    private int indexedBlocks, indexedInk;
    private readonly record struct FontKey(string Family, float Size, bool Bold, bool Italic);
    private readonly Dictionary<FontKey, (SKFont Font, long Used)> fonts = new();
    private readonly Dictionary<string, (SKPicture Picture, int Points, long Used)> inkPictures = new();
    private long useClock;
    private int picturePoints;
    public RendererStatistics Statistics { get; } = new();

    /// <summary>Get retained geometry with an explicit content token. The default path
    /// rebuilds so callers using mutable DTOs without notifications cannot see stale bounds.</summary>
    public PageContentIndex ContentIndex(NotePage page, long? revision = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(page);
        if (revision.HasValue && ReferenceEquals(indexedPage, page) && indexedRevision == revision
            && indexedBlocks == page.Blocks.Count && indexedInk == page.Ink.Count && contentIndex is not null) return contentIndex;
        contentIndex = new(page); indexedPage = page; indexedRevision = revision;
        indexedBlocks = page.Blocks.Count; indexedInk = page.Ink.Count;
        ClearInkPictures(); Statistics.SpatialBuilds++; return contentIndex;
    }
    private SKFont Font(TextFormat format)
    {
        var key = new FontKey(format.FontFamily, format.FontSize, format.Bold, format.Italic);
        if (fonts.TryGetValue(key, out var cached)) { fonts[key] = (cached.Font, ++useClock); return cached.Font; }
        if (fonts.Count >= 256)
        {
            var oldest = fonts.MinBy(x => x.Value.Used); oldest.Value.Font.Dispose(); fonts.Remove(oldest.Key);
        }
        var font = new SKFont(Face(format), format.FontSize);
        fonts.Add(key, (font, ++useClock)); Statistics.FontBuilds++; return font;
    }
    private void ClearFonts() { foreach (var font in fonts.Values) font.Font.Dispose(); fonts.Clear(); }
    private void ClearInkPictures()
    {
        foreach (var picture in inkPictures.Values) picture.Picture.Dispose(); inkPictures.Clear(); picturePoints = 0;
    }
    private void DrawRetainedInk(SKCanvas canvas, InkStroke stroke)
    {
        if (stroke.Points.Count == 0) return;
        if (inkPictures.TryGetValue(stroke.Id, out var cached))
        {
            inkPictures[stroke.Id] = (cached.Picture, cached.Points, ++useClock); canvas.DrawPicture(cached.Picture); return;
        }
        // Bound native recording complexity, not a claimed exact native byte count.
        while (inkPictures.Count > 0 && (inkPictures.Count >= 256 || picturePoints + stroke.Points.Count > 200000))
        {
            var oldest = inkPictures.MinBy(x => x.Value.Used); oldest.Value.Picture.Dispose();
            picturePoints -= oldest.Value.Points; inkPictures.Remove(oldest.Key);
        }
        var bounds = InkGeometry.Bounds(stroke);
        using var recorder = new SKPictureRecorder();
        var recording = recorder.BeginRecording(SKRect.Create(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)));
        DrawInk(recording, stroke); var picture = recorder.EndRecording();
        Statistics.InkPictureBuilds++;
        canvas.DrawPicture(picture);
        if (stroke.Points.Count > 200000) { picture.Dispose(); return; }
        inkPictures.Add(stroke.Id, (picture, stroke.Points.Count, ++useClock)); picturePoints += stroke.Points.Count;
    }
}
