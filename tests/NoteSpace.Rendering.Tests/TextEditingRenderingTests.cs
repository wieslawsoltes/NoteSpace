using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Rendering.Skia;
using SkiaSharp;

internal static class TextEditingRenderingTests
{
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static byte[] Pixels(SKSurface s) { using var i = s.Snapshot(); using var d = i.Encode(SKEncodedImageFormat.Png, 100); return d.ToArray(); }
    public static void Run(Action<string, Action> test)
    {
        test("Caret hit testing round trips mixed-sized formatted text", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "abc XYZ", Width = 600 };
            RichText.Apply(b, 4, 3, f => { f.FontSize = 40; f.Bold = true; });
            foreach (var i in Enumerable.Range(0, b.Text.Length + 1)) { var c = r.CaretBounds(b, i); Check(r.HitTestText(b, c.X + 0.01f, c.Y + c.Height / 2) == i, $"offset {i}"); }
        });
        test("Caret and selection use the same center and right alignment", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "text", Width = 500 }; var left = r.CaretBounds(b, 0).X;
            b.Format.Alignment = 1; var center = r.CaretBounds(b, 0).X; b.Format.Alignment = 2; var right = r.CaretBounds(b, 0).X;
            Check(center > left && right > center); Check(r.SelectionBounds(b, 0, 4).Single().X == right);
        });
        test("Visual-line navigation follows wrapping and not native input geometry", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "alpha beta gamma delta epsilon zeta", Width = 130 }; var c = r.CaretBounds(b, 0);
            var down = r.MoveTextLine(b, 0, 1, c.X); Check(down > 0 && r.CaretBounds(b, down).Y > c.Y);
            Check(r.MoveTextLine(b, down, -1, c.X) == 0 && r.TextLineEdge(b, down, false) == down);
        });
        test("Blank and trailing paragraphs have usable carets", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "a\r\n\r\n" };
            Check(r.CaretBounds(b, 5).Y > r.CaretBounds(b, 3).Y && r.CaretBounds(b, 3).Y > r.CaretBounds(b, 0).Y);
            b.Text = ""; var c = r.CaretBounds(b, 0); Check(c.Height > 0 && r.HitTestText(b, 200, 500) == 0);
        });
        test("Tab interval gives real caret advances without missing-glyph text", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "a\tb", TextFlow = new() { TabWidth = 72 } };
            Check(Math.Abs(r.CaretBounds(b, 2).X - 84) < 0.1f);
            var tab = r.Layout(b).Fragments.Single(f => f.IsTab); Check(tab.SourceStart == 1 && tab.Width > 0);
        });
        test("Bullets and numbering belong to paragraphs rather than wrapped lines", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "a long paragraph with several wrapped lines\nnext", Width = 130, Format = new() { Numbered = true } };
            var labels = r.Layout(b).Fragments.Where(f => f.SourceStart < 0).Select(f => f.Text).ToArray(); Check(labels.SequenceEqual(["1.", "2."]));
        });
        test("First-line indent does not indent continuation lines", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "one two three four five six seven", Width = 160, TextFlow = new() { LeftIndent = 15, FirstLineIndent = 25 } };
            var first = r.CaretBounds(b, 0); var next = r.MoveTextLine(b, 0, 1, 0);
            Check(first.X == 52 && r.CaretBounds(b, next).X == 27);
        });
        test("Spacing increases line layout and exact selection height", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "a\nb" }; var old = r.Layout(b).Height;
            b.TextFlow.LineSpacing = 2; b.TextFlow.SpaceBefore = 6; b.TextFlow.SpaceAfter = 10;
            Check(r.Layout(b).Height > old && r.SelectionBounds(b, 0, 1).Single().Height == 32);
        });
        test("Layout cache invalidates when flow values change in place", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "a\tb" }; var first = r.Layout(b); b.TextFlow.TabWidth = 120;
            var next = r.Layout(b); Check(!ReferenceEquals(first, next)); Check(ReferenceEquals(next, r.Layout(b)));
        });
        test("Superscript and subscript retain nominal font size with distinct baselines", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "x23" }; RichText.Apply(b, 1, 1, f => f.Baseline = 1); RichText.Apply(b, 2, 1, f => f.Baseline = -1);
            var runs = r.Layout(b).Fragments; Check(runs[1].Baseline < runs[0].Baseline && runs[2].Baseline > runs[0].Baseline && runs[1].Format.FontSize == 16);
        });
        test("Draft and committed rich text produce identical pixels without adorners", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(700, 500));
            var b = new NoteBlock { Text = "styled text", Width = 400, Height = 120 }; var page = new NotePage { Blocks = [b] }; var draft = new TextEditingBuffer(b);
            draft.Select(0, 6); draft.Format(f => { f.Bold = true; f.Color = 0xFF7030A0; f.FontSize = 28; });
            r.Render(s.Canvas, page, 700, 500, new() { ContentRevision = 1, TextEdit = new(draft.Block) { CaretVisible = false } }); var live = Pixels(s);
            draft.CopyContentTo(b); r.Render(s.Canvas, page, 700, 500, new() { ContentRevision = 2 }); Check(live.SequenceEqual(Pixels(s)));
        });
        test("Selection and caret adorners are transient and never enter page content", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(600, 400)); var b = new NoteBlock { Text = "selected" }; var page = new NotePage { Blocks = [b] }; var json = DocumentJson.PageJson(page);
            r.Render(s.Canvas, page, 600, 400); var plain = Pixels(s);
            r.Render(s.Canvas, page, 600, 400, new() { TextEdit = new(b) { Selection = r.SelectionBounds(b, 0, 4), CaretVisible = false } });
            Check(!plain.SequenceEqual(Pixels(s)) && DocumentJson.PageJson(page) == json);
            r.Render(s.Canvas, page, 600, 400, new() { TextEdit = new(b) { Caret = r.CaretBounds(b, 2), CaretVisible = true } }); Check(!plain.SequenceEqual(Pixels(s)));
        });
        test("Draft growth remains visible beyond the committed container bounds", () => {
            using var r = new PageRenderer(); using var s = SKSurface.Create(new SKImageInfo(500, 150)); var b = new NoteBlock { Text = "a", Height = 40 };
            var draft = DocumentJson.CloneBlock(b); draft.Text = string.Join('\n', Enumerable.Repeat("line", 30)); draft.Height = r.MeasureHeight(draft);
            r.Render(s.Canvas, new NotePage { Blocks = [b] }, 500, 150, new() { OffsetY = 400, ContentRevision = 1, TextEdit = new(draft) { CaretVisible = false } });
            Check(r.Statistics.TextFragmentsDrawn > 0 && r.Statistics.BlocksDrawn == 1);
        });
        test("Rich clipboard pixels agree with directly formatted text", () => {
            using var r = new PageRenderer(); var b = new NoteBlock { Text = "Hello", Format = new() { FontSize = 30 } }; RichText.Apply(b, 0, 5, f => f.Bold = true);
            var target = new NoteBlock(); RichText.PasteRange(target, 0, 0, RichText.CopyRange(b, 0, 5));
            Check(Math.Abs(r.CaretBounds(b, 5).X - r.CaretBounds(target, 5).X) < 0.01f);
        });
        test("HTML export preserves script formatting and container indentation", () => {
            var b = new NoteBlock { Text = "x2", TextFlow = new() { LeftIndent = 24 }, Format = new() { Alignment = 2 } }; RichText.Apply(b, 1, 1, f => f.Baseline = 1);
            var html = NoteSpace.Storage.NoteExport.Html(new NotePage { Blocks = [b] }); Check(html.Contains("vertical-align:super") && html.Contains("padding-left:24px") && html.Contains("text-align:right"));
        });
    }
}
