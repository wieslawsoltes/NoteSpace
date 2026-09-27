using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Rendering.Skia;
using NoteSpace.Storage;
using SkiaSharp;

var passed = 0; var failed = 0;
void Test(string name, Action run) { try { run(); Console.WriteLine("PASS " + name); passed++; } catch (Exception error) { Console.Error.WriteLine("FAIL " + name + ": " + error); failed++; } }
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }

Test("Pen hit testing uses segment interiors", () => { var s = new InkStroke { Points = [new(0, 0), new(100, 0)], Width = 2 }; Check(InkGeometry.HitTest(s, new(50, 2), 2)); Check(!InkGeometry.HitTest(s, new(50, 20), 2)); });
Test("Rectangle eraser avoids empty interior", () => { var s = new InkStroke { Tool = DrawingTool.Rectangle, Points = [new(10, 10), new(110, 110)] }; Check(InkGeometry.HitTest(s, new(60, 10), 2)); Check(!InkGeometry.HitTest(s, new(60, 60), 2)); });
Test("Ellipse eraser follows ellipse boundary", () => { var s = new InkStroke { Tool = DrawingTool.Ellipse, Points = [new(0, 0), new(100, 60)] }; Check(InkGeometry.HitTest(s, new(100, 30), 2)); Check(!InkGeometry.HitTest(s, new(50, 30), 2)); });
Test("Point strokes have hittable bounds", () => Check(InkGeometry.HitTest(new InkStroke { Points = [new(10, 10)] }, new(11, 11), 3)));
Test("Simplification preserves endpoints", () => { var points = Enumerable.Range(0, 100).Select(i => new InkPoint(i, 0)).ToArray(); var simple = InkGeometry.Simplify(points); Check(simple.Count == 2); Check(simple[0] == points[0] && simple[^1] == points[^1]); });
Test("Simplification retains pressure changes", () => { var points = new InkPoint[] { new(0, 0, 0.2f), new(10, 0, 1), new(20, 0, 0.2f) }; Check(InkGeometry.Simplify(points).Count == 3); });
Test("Stroke bounds include pen width", () => { var b = InkGeometry.Bounds(new InkStroke { Width = 4, Points = [new(10, 20), new(40, 50)] }); Check(b.Contains(6, 16) && b.Contains(44, 54)); });
Test("PNG export has correct signature and dimensions", () => { using var r = new PageRenderer(); var p = SampleWorkspace.Create().Notebooks[0].Sections[0].Pages[0]; var bytes = r.ExportPng(p); Check(bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78); using var bitmap = SKBitmap.Decode(bytes); Check(bitmap.Width >= 1100 && bitmap.Height >= 760); Check(bitmap.GetPixel(0, 0) == SKColors.White); });
Test("Oversized PNG is rejected before allocation", () => { using var r = new PageRenderer(); var p = new NotePage { Blocks = [new NoteBlock { X = 90000, Y = 90000 }] }; Throws<InvalidOperationException>(() => r.ExportPng(p)); });
Test("Invalid export scale is rejected", () => { using var r = new PageRenderer(); Throws<ArgumentOutOfRangeException>(() => r.ExportPng(new NotePage(), float.NaN)); });
Test("Text wraps to the container width", () => { using var r = new PageRenderer(); var b = new NoteBlock { Text = "A fairly long sentence repeated several times to force line wrapping.", Width = 150 }; Check(r.Layout(b).Height > 60); });
Test("Text layout preserves graphemes", () => { using var r = new PageRenderer(); var b = new NoteBlock { Text = "A👩‍💻é😀B", Width = 30 }; var layout = r.Layout(b); Check(string.Concat(layout.Fragments.Select(f => f.Text)) == b.Text); });
Test("Rich format changes invalidate text layout", () => { using var r = new PageRenderer(); var b = new NoteBlock { Text = "Hello", Width = 500 }; var old = r.MeasureHeight(b); RichText.Apply(b, 0, 5, f => f.FontSize = 48); Check(r.MeasureHeight(b) > old); });
Test("Viewport rendering does not mutate page", () => { using var r = new PageRenderer(); var p = SampleWorkspace.Create().Notebooks[0].Sections[0].Pages[0]; var before = DocumentJson.PageJson(p); using var surface = SKSurface.Create(new SKImageInfo(300, 200)); r.Render(surface.Canvas, p, 300, 200, new RenderOptions { OffsetX = 1000, OffsetY = 500, Zoom = 0.5f }); Check(DocumentJson.PageJson(p) == before); });
Test("Renderer owns disposable resources", () => { using var surface = SKSurface.Create(new SKImageInfo(100, 100)); var r = new PageRenderer(); r.Dispose(); r.Dispose(); Throws<ObjectDisposedException>(() => r.Render(surface.Canvas, new NotePage(), 100, 100)); });
Test("Invalid image data does not crash rendering", () => { using var r = new PageRenderer(); var p = new NotePage { Blocks = [new NoteBlock { Kind = BlockKind.Image, Data = [1, 2, 3, 4] }] }; Check(r.ExportPng(p).Length > 100); });
Test("HTML export escapes title and note text", () => { var p = new NotePage { Title = "<script>alert(1)</script>", Blocks = [new NoteBlock { Text = "<img src=x onerror=alert(1)> & text" }] }; var html = NoteExport.Html(p); Check(!html.Contains("<script>")); Check(html.Contains("&lt;script&gt;")); Check(!html.Contains("<img src=x")); Check(html.Contains("&amp; text")); });
Test("Unsafe link schemes are rejected", () => { Check(!NoteExport.SafeLink("javascript:alert(1)")); Check(!NoteExport.SafeLink("data:text/html,test")); Check(NoteExport.SafeLink("https://example.com/")); Check(NoteExport.SafeLink("mailto:notes@example.com")); });
Test("Rich-text unsafe links never enter HTML hrefs", () => { var b = new NoteBlock { Text = "test" }; RichText.Apply(b, 0, 4, f => f.Bold = true, "javascript:alert(1)"); Check(!NoteExport.Html(new NotePage { Blocks = [b] }).Contains("javascript:")); });
Test("Markdown checklist and table export", () => { var p = new NotePage { Blocks = [new NoteBlock { Kind = BlockKind.Checklist, Text = "Done", Checked = true }, new NoteBlock { Kind = BlockKind.Table, Cells = [["A", "B"], ["one|two", "value"]] }] }; var md = NoteExport.Markdown(p); Check(md.Contains("- [x] Done")); Check(md.Contains("one\\|two")); });
Test("Text importer recognizes title and checklist", () => { var p = NoteExport.ImportText("# Imported\n\n- [x] Done\n\n## Heading", "fallback"); Check(p.Title == "Imported"); Check(p.Blocks[0].Kind == BlockKind.Checklist && p.Blocks[0].Checked); Check(p.Blocks[1].Kind == BlockKind.Heading); });
Test("Safe file names remove filesystem separators", () => Check(!NoteExport.SafeFileName("a/b:c\\d?e").Any(c => "/:\\?".Contains(c))));
Test("Failed file load never overwrites corrupt storage", () => { var folder = Path.Combine(Path.GetTempPath(), Ids.New()); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "notes.json"); File.WriteAllText(path, "corrupt"); try { var store = new FileWorkspaceStore(path); Throws<InvalidDataException>(() => store.SaveAsync(SampleWorkspace.Create(), null).GetAwaiter().GetResult()); Check(File.ReadAllText(path) == "corrupt"); } finally { Directory.Delete(folder, true); } });
Test("Large aggregate file saves fail without replacement", () => { var folder = Path.Combine(Path.GetTempPath(), Ids.New()); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "notes.json"); try { var store = new FileWorkspaceStore(path); var w = SampleWorkspace.Create(); var token = store.SaveAsync(w, null).GetAwaiter().GetResult(); var previous = File.ReadAllText(path); w.Notebooks[0].Sections[0].Pages[0].Blocks = Enumerable.Range(0, 17).Select(_ => new NoteBlock { Text = new string('x', 2 * 1024 * 1024) }).ToList(); Throws<InvalidDataException>(() => store.SaveAsync(w, token).GetAwaiter().GetResult()); Check(File.ReadAllText(path) == previous); } finally { Directory.Delete(folder, true); } });

RetainedRenderingTests.Run(Test);
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
