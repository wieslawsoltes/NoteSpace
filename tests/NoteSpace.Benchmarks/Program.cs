using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Rendering.Skia;
using SkiaSharp;

// This exact runner also compiles against the pre-change baseline. Optional new
// diagnostics are read by reflection only here, never in the browser application.
var results = new List<object>();
object Measure(string name, Action action, int iterations)
{
    action();
    var times = new List<double>(); var allocations = new List<long>();
    for (var sample = 0; sample < 7; sample++)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++) action();
        times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds / iterations);
        allocations.Add((GC.GetAllocatedBytesForCurrentThread() - bytes) / iterations);
    }
    times.Sort(); allocations.Sort();
    var result = new { name, medianMilliseconds = times[3], medianManagedBytes = allocations[3], samples = 7, iterationsPerSample = iterations };
    Console.WriteLine($"{name}: {times[3]:F4} ms, {allocations[3]} managed bytes/op"); results.Add(result); return result;
}
RenderOptions Retained(float offsetY = 0)
{
    var options = new RenderOptions { OffsetY = offsetY };
    typeof(RenderOptions).GetProperty("ContentRevision")?.SetValue(options, 1L); return options;
}
using (var renderer = new PageRenderer())
using (var surface = SKSurface.Create(new SKImageInfo(1000, 700)))
{
    var page = new NotePage { Title = "Performance fixture", Created = new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), Blocks = Enumerable.Range(0, 10000).Select(i => new NoteBlock { Kind = BlockKind.Divider, X = i % 100 * 900, Y = i / 100 * 900 + 140, Width = 250, Height = 30 }).ToList() };
    var options = Retained(); Measure("sparse-10000-block-frame", () => renderer.Render(surface.Canvas, page, 1000, 700, options), 20);
}
using (var renderer = new PageRenderer())
using (var surface = SKSurface.Create(new SKImageInfo(1000, 700)))
{
    var page = new NotePage { Ink = Enumerable.Range(0, 1200).Select(i => new InkStroke { Points = Enumerable.Range(0, 500).Select(j => new InkPoint(i % 40 * 1500 + j, i / 40 * 1500 + 180 + MathF.Sin(j * 0.05f) * 40, (j % 100) / 100f)).ToList() }).ToList() };
    var options = Retained(); Measure("600000-ink-point-frame", () => renderer.Render(surface.Canvas, page, 1000, 700, options), 10);
}
using (var renderer = new PageRenderer())
{
    var block = new NoteBlock { Text = string.Join(' ', Enumerable.Repeat("text", 6000)), Width = 700, Height = 20000 };
    for (var i = 0; i < 2000; i++) block.Marks.Add(new() { Start = i * 10, Length = 4, Format = new() { Bold = i % 2 == 0, Italic = i % 3 == 0 } });
    Measure("cold-6000-token-2000-mark-layout", () => { renderer.ClearCaches(); _ = renderer.Layout(block); }, 2);
}
using (var renderer = new PageRenderer())
using (var surface = SKSurface.Create(new SKImageInfo(1000, 240)))
{
    var page = new NotePage { Blocks = [new NoteBlock { Text = string.Join('\n', Enumerable.Repeat("A long notebook line with several words", 1000)), Width = 700, Height = 20000 }] };
    var options = Retained(8000); Measure("1000-line-note-scrolled-frame", () => renderer.Render(surface.Canvas, page, 1000, 240, options), 20);
}
var pages = Enumerable.Range(0, 100).Select(_ => new NotePage { Blocks = [new NoteBlock { Text = new string('x', 10000) }] }).ToList();
pages[0].Blocks.Clear();
var session = new EditorSession(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = pages }] }] });
Measure("search-missing-in-1mb-workspace", () => { if (session.Search("needle-not-present").Any()) throw new Exception("Unexpected match"); }, 20);
var selected = session.SelectedPage!.Id; var editNumber = 0;
Measure("one-page-edit-in-1mb-workspace", () => session.RenamePage(selected, "Edit " + editNumber++), 5);
var historyProperty = typeof(EditorSession).GetProperty("RetainedHistoryCharacters");
var retained = historyProperty?.GetValue(session) ?? typeof(EditorSession).GetField("historyBytes", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(session);
var undoCount = 0; while (session.CanUndo) { session.Undo(); undoCount++; }
var lookupPages = Enumerable.Range(0, 10000).Select(_ => new NotePage()).ToList();
var lookup = new EditorSession(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = lookupPages }] }] });
var lastId = lookupPages[^1].Id;
Measure("find-last-of-10000-pages", () => { if (lookup.FindPage(lastId) is null) throw new Exception("Missing page"); }, 100);
var report = new {
    schemaVersion = 1, label = args.Length > 1 ? args[1] : "working-tree", runtime = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
    note = "Headless CPU Skia microbenchmarks, seven-sample medians. Managed allocation excludes native/GPU memory. Not browser FPS, startup, stylus latency or an end-to-end OneNote comparison.",
    results, history = new { edits = editNumber, undoEntriesRetained = undoCount, retainedSerializedCharacters = retained }
};
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
if (args.Length > 0) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!); File.WriteAllText(args[0], json); }
else Console.WriteLine(json);
