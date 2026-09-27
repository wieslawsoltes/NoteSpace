using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Rendering.Skia;
using SkiaSharp;

internal static class RetainedRenderingTests
{
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static byte[] Pixels(SKSurface surface) { using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray(); }
    public static void Run(Action<string, Action> test)
    {
        test("Retained geometry and fonts are reused across unchanged frames", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(800, 600));
            var p = SampleWorkspace.Create().Notebooks[0].Sections[0].Pages[0]; var options = new RenderOptions { ContentRevision = 1 };
            r.Render(s.Canvas, p, 800, 600, options); var spatial = r.Statistics.SpatialBuilds; var fonts = r.Statistics.FontBuilds; var layouts = r.Statistics.LayoutBuilds;
            for (var i = 0; i < 10; i++) r.Render(s.Canvas, p, 800, 600, options);
            Check(r.Statistics.SpatialBuilds == spatial && r.Statistics.FontBuilds == fonts && r.Statistics.LayoutBuilds == layouts && r.Statistics.LayoutHits > 0);
        });
        test("Direct mutable renderer callers do not need an invalidation token", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(400, 300));
            var b = new NoteBlock { Kind = BlockKind.Divider, X = 10000, Y = 150 }; var p = new NotePage { Blocks = [b] };
            r.Render(s.Canvas, p, 400, 300); Check(r.Statistics.BlocksDrawn == 0);
            b.X = 40; r.Render(s.Canvas, p, 400, 300); Check(r.Statistics.BlocksDrawn == 1 && r.Statistics.SpatialBuilds == 2);
        });
        test("Retained and immediate ink produce identical pixels", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(800, 600));
            var p = new NotePage();
            foreach (var tool in new[] { DrawingTool.Pen, DrawingTool.Highlighter, DrawingTool.Rectangle, DrawingTool.Ellipse, DrawingTool.Line })
                p.Ink.Add(new InkStroke { Tool = tool, Width = tool == DrawingTool.Highlighter ? 20 : 4, Points = [new(60, 200, 0.1f), new(200, 280, 0.9f), new(320, 220, 0.4f)] });
            r.Render(s.Canvas, p, 800, 600); var immediate = Pixels(s);
            r.Render(s.Canvas, p, 800, 600, new() { ContentRevision = 1 }); Check(immediate.SequenceEqual(Pixels(s)));
            var builds = r.Statistics.InkPictureBuilds; r.Render(s.Canvas, p, 800, 600, new() { ContentRevision = 1 });
            Check(builds == r.Statistics.InkPictureBuilds && immediate.SequenceEqual(Pixels(s)));
        });
        test("Content-token changes rebuild edited ink recordings", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(400, 400));
            var p = new NotePage { Ink = [new InkStroke { Points = [new(100, 200), new(200, 250)] }] };
            r.Render(s.Canvas, p, 400, 400, new() { ContentRevision = 1 }); var first = Pixels(s);
            p.Ink[0].Color = 0xFFFF0000; p.Ink[0].Points[1] = new(250, 300);
            r.Render(s.Canvas, p, 400, 400, new() { ContentRevision = 2 }); var edited = Pixels(s);
            Check(!first.SequenceEqual(edited) && r.Statistics.InkPictureBuilds == 2);
            r.Render(s.Canvas, p, 400, 400); Check(edited.SequenceEqual(Pixels(s)));
        });
        test("Long text draws only intersecting lines", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(800, 240));
            var b = new NoteBlock { Text = string.Join('\n', Enumerable.Repeat("A long notebook line of text", 1000)), Width = 600, Height = 20000 };
            var p = new NotePage { Blocks = [b] }; var count = r.Layout(b).Fragments.Count;
            r.Render(s.Canvas, p, 800, 240, new() { ContentRevision = 1, OffsetY = 8000 });
            Check(r.Statistics.TextFragmentsDrawn > 0 && r.Statistics.TextFragmentsDrawn < count / 20);
        });
        test("Text layout snapshots do not alias mutable format objects", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "hello" }; var old = r.Layout(b);
            b.Format.Color = 0xFFFF0000; var next = r.Layout(b);
            Check(!ReferenceEquals(old, next) && old.Fragments[0].Format.Color != next.Fragments[0].Format.Color);
        });
        test("Legacy overlapping marks preserve last-mark-wins rendering", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "abcdef", Marks = [new() { Start = 0, Length = 6, Format = new() { Bold = true } }, new() { Start = 2, Length = 2, Format = new() { Italic = true } }] };
            var layout = r.Layout(b); Check(string.Concat(layout.Fragments.Select(f => f.Text)) == b.Text);
            Check(layout.Fragments.Any(f => f.Text == "cd" && f.Format.Italic && !f.Format.Bold));
        });
        test("In-place mark changes invalidate unversioned text caches", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "abc", Marks = [new() { Start = 0, Length = 3, Format = new() { FontSize = 16 } }] };
            var old = r.Layout(b); b.Marks[0].Format.FontSize = 60; Check(r.Layout(b).Height > old.Height);
        });
        test("Reused live resize previews invalidate their layout at each width", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(800, 600));
            var b = new NoteBlock { Text = "A line that will wrap as the resize grip moves.", Width = 600, Height = 300 }; var p = new NotePage { Blocks = [b] };
            var preview = DocumentJson.CloneBlock(b); var options = new RenderOptions { ContentRevision = 1, PreviewBlock = preview };
            r.Render(s.Canvas, p, 800, 600, options); var builds = r.Statistics.LayoutBuilds;
            preview.Width = 150; r.Render(s.Canvas, p, 800, 600, options); Check(r.Statistics.LayoutBuilds > builds);
        });
        test("A preview moved into view is not culled with the original bounds", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(400, 300));
            var b = new NoteBlock { X = 10000, Y = 160, Text = "Moved" }; var p = new NotePage { Blocks = [b] }; var preview = DocumentJson.CloneBlock(b); preview.X = 10;
            r.Render(s.Canvas, p, 400, 300, new() { ContentRevision = 1, PreviewBlock = preview }); Check(r.Statistics.BlocksDrawn == 1);
        });
        test("Large tables draw only visible rows and columns", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(200, 80));
            var b = new NoteBlock { Kind = BlockKind.Table, X = 0, Y = 140, Width = 5000, Height = 4200, Cells = Enumerable.Range(0, 100).Select(_ => Enumerable.Repeat("cell", 50).ToList()).ToList() };
            r.Render(s.Canvas, new NotePage { Blocks = [b] }, 200, 80, new() { ContentRevision = 1, OffsetX = 2000, OffsetY = 1000 });
            Check(r.Statistics.TableCellsDrawn is > 0 and <= 12);
        });
        test("Clearing caches permits clean rebuild and disposal is idempotent", () => {
            var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(500, 400)); var p = new NotePage { Blocks = [new NoteBlock { Text = "hello" }] };
            r.Render(s.Canvas, p, 500, 400, new() { ContentRevision = 1 }); var before = r.Statistics.LayoutBuilds; r.ClearCaches();
            r.Render(s.Canvas, p, 500, 400, new() { ContentRevision = 1 }); Check(r.Statistics.LayoutBuilds > before); r.Dispose(); r.Dispose();
            Throws<ObjectDisposedException>(() => r.Layout(p.Blocks[0]));
        });
        test("Invalid view transforms are rejected before altering canvas state", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(50, 50)); var count = s.Canvas.SaveCount;
            Throws<ArgumentOutOfRangeException>(() => r.Render(s.Canvas, new NotePage(), 50, 50, new() { Zoom = 0 }));
            Throws<ArgumentOutOfRangeException>(() => r.Render(s.Canvas, new NotePage(), 50, 50, new() { OffsetX = float.MaxValue })); Check(s.Canvas.SaveCount == count);
        });
    }
}
