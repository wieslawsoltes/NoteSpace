using NoteSpace.Core;
using NoteSpace.Editor;

internal static class PerformanceAndTableTests
{
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static NoteBlock Table() => new() { Kind = BlockKind.Table, X = 100, Y = 200, Width = 400, Height = 126, Cells = [["A", "B"], ["one", "two"], ["three", "four"]] };
    private static string Cells(NoteBlock b) => string.Join(";", b.Cells.Select(r => string.Join("|", r)));
    public static void Run(Action<string, Action> test)
    {
        test("Page edits retain page-only history and leave unrelated DTOs intact", () => {
            var pages = Enumerable.Range(0, 100).Select(_ => new NotePage { Blocks = [new NoteBlock { Text = new string('x', 10000) }] }).ToList();
            pages[0].Blocks.Clear();
            var s = new EditorSession(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = pages }] }] });
            var other = s.Pages.Last().Page; var id = s.SelectedPage!.Id;
            for (var i = 0; i < 20; i++) s.RenamePage(id, "Rename " + i);
            Check(s.UndoCount == 20 && s.RetainedHistoryCharacters < 50000);
            Check(ReferenceEquals(other, s.Pages.Last().Page));
            for (var i = 0; i < 20; i++) s.Undo();
            Check(s.SelectedPage!.Title == "Untitled page" && s.RedoCount == 20);
            for (var i = 0; i < 20; i++) s.Redo();
            Check(s.SelectedPage!.Title == "Rename 19");
        });
        test("An unchanged page edit creates no history, timestamp or revision", () => {
            var s = new EditorSession(SampleWorkspace.Create()); var before = DocumentJson.Serialize(s.Document);
            s.EditPage(s.SelectedPage!.Id, "No-op", _ => { }); Check(before == DocumentJson.Serialize(s.Document) && !s.CanUndo);
        });
        test("Detached page drafts do not leak failed content changes", () => {
            var s = new EditorSession(SampleWorkspace.Create()); var before = DocumentJson.Serialize(s.Document); var p = s.SelectedPage!;
            Throws<InvalidDataException>(() => s.EditPage(p.Id, "Invalid", draft => { draft.Blocks.Clear(); draft.Title = new string('x', 501); }));
            Check(before == DocumentJson.Serialize(s.Document) && ReferenceEquals(p, s.SelectedPage) && !s.CanUndo);
        });
        test("Page drafts cannot change page identity", () => {
            var s = new EditorSession(SampleWorkspace.Create()); var id = s.SelectedPage!.Id;
            Throws<InvalidOperationException>(() => s.EditPage(id, "Identity", p => p.Id = Ids.New())); Check(s.FindPage(id) is not null && !s.CanUndo);
        });
        test("Page draft IDs are validated against other pages and trash", () => {
            var s = new EditorSession(SampleWorkspace.Create()); var first = s.SelectedPage!; var other = s.Pages.Last().Page;
            Throws<InvalidDataException>(() => s.EditPage(first.Id, "Duplicate identity", p => p.Blocks.Add(new NoteBlock { Id = other.Id })));
            Check(!s.CanUndo && s.Document.Revision == 0);
        });
        test("Nested page transactions fail atomically", () => {
            var s = new EditorSession(SampleWorkspace.Create()); var before = DocumentJson.Serialize(s.Document);
            Throws<InvalidOperationException>(() => s.EditPage(s.SelectedPage!.Id, "Outer", p => { p.Title = "Draft"; s.RenamePage(p.Id, "Nested"); }));
            Check(before == DocumentJson.Serialize(s.Document));
        });
        test("Mixed page and organization history replays in both directions", () => {
            var s = new EditorSession(SampleWorkspace.Create()); var first = s.SelectedPage!.Id; var old = s.SelectedPage.Title; var section = s.Pages.First().Section.Id;
            s.RenamePage(first, "Renamed"); var second = s.AddPage(section, "Second").Id; s.AddBlock(second, new NoteBlock { Text = "content" }); s.DeletePage(second);
            s.Undo(); Check(s.FindPage(second)!.Blocks.Count == 1); s.Undo(); Check(s.FindPage(second)!.Blocks.Count == 0);
            s.Undo(); Check(s.FindPage(second) is null); s.Undo(); Check(s.FindPage(first)!.Title == old);
            for (var i = 0; i < 4; i++) s.Redo(); Check(s.FindPage(first)!.Title == "Renamed" && s.FindPage(second) is null);
        });
        test("Page edit rollback preserves redo and emits no change event", () => {
            var s = new EditorSession(SampleWorkspace.Create()); var id = s.SelectedPage!.Id;
            s.RenamePage(id, "redo"); s.Undo(); var events = 0; s.Changed += (_, _) => events++;
            Throws<InvalidDataException>(() => s.EditPage(id, "Bad", p => p.Blocks[0].Width = -1));
            Check(events == 0 && s.CanRedo); s.Redo(); Check(s.FindPage(id)!.Title == "redo");
        });
        test("Page edits retain full aggregate size validation", () => {
            var pages = Enumerable.Range(0, 12).Select(_ => new NotePage { Blocks = [new NoteBlock { Text = new string('x', 2 * 1024 * 1024) }] }).ToList();
            var target = new NotePage(); pages.Add(target);
            var s = new EditorSession(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = pages }] }] });
            Throws<InvalidDataException>(() => s.EditPage(target.Id, "Too large", p => p.Blocks = Enumerable.Range(0, 5).Select(_ => new NoteBlock { Text = new string('x', 2 * 1024 * 1024) }).ToList()));
            Check(s.FindPage(target.Id)!.Blocks.Count == 0 && s.Document.Revision == 0 && !s.CanUndo);
        });
        test("Lookup projections see mutations inside transactions and after rollback", () => {
            var s = new EditorSession(SampleWorkspace.Create()); _ = s.SelectedPage; var n = new Notebook { Sections = [new NoteSection { Pages = [new NotePage()] }] };
            s.Execute("Add", w => { w.Notebooks.Add(n); Check(s.FindSection(n.Sections[0].Id) == n.Sections[0]); Check(s.FindPage(n.Sections[0].Pages[0].Id) is not null); }, true);
            var id = n.Sections[0].Pages[0].Id; Check(s.FindPage(id) is not null); s.Undo(); Check(s.FindPage(id) is null); s.Redo(); Check(s.FindPage(id) is not null);
        });
        test("Hosts can explicitly refresh indexes after external DTO mutations", () => {
            var s = new EditorSession(SampleWorkspace.Create()); _ = s.SelectedPage; var p = new NotePage();
            s.Document.Notebooks[0].Sections[0].Pages.Add(p); s.InvalidateIndexes(); Check(s.FindPage(p.Id) == p);
        });
        test("Table cells edit and normalize legacy ragged rows", () => {
            var b = Table(); b.Cells[1].RemoveAt(1); NoteTable.SetCell(b, new(1, 1), "filled"); Check(b.Cells[1][1] == "filled" && b.Cells.All(r => r.Count == 2));
        });
        test("Table hit testing maps document coordinates without accepting edges", () => {
            var b = Table(); Check(NoteTable.HitTest(b, 350, 250) == new TableCellAddress(1, 1));
            Check(NoteTable.HitTest(b, 500, 250) is null && NoteTable.HitTest(b, 110, 326) is null);
            Check(NoteTable.CellBounds(b, new(1, 1)) == new NoteRect(300, 242, 200, 42));
        });
        test("Table row and column insertion preserves neighboring data", () => {
            var b = Table(); NoteTable.InsertRow(b, 1); NoteTable.InsertColumn(b, 1);
            Check(Cells(b) == "A||B;||;one||two;three||four" && b.Height == 168);
            NoteTable.DeleteRow(b, 1); NoteTable.DeleteColumn(b, 1); Check(Cells(b) == Cells(Table()));
        });
        test("Deleting the final row or column is rejected", () => {
            var b = new NoteBlock { Kind = BlockKind.Table, Cells = [["keep"]] };
            Throws<InvalidOperationException>(() => NoteTable.DeleteRow(b, 0)); Throws<InvalidOperationException>(() => NoteTable.DeleteColumn(b, 0)); Check(b.Cells[0][0] == "keep");
        });
        test("Table transpose is reversible", () => {
            var b = Table(); NoteTable.Transpose(b); Check(Cells(b) == "A|one|three;B|two|four"); NoteTable.Transpose(b); Check(Cells(b) == Cells(Table()));
        });
        test("Table transpose refuses an over-wide result without mutation", () => {
            var b = Table(); b.Cells = Enumerable.Range(0, 51).Select(i => new List<string> { i.ToString() }).ToList(); var old = Cells(b);
            Throws<InvalidOperationException>(() => NoteTable.Transpose(b)); Check(Cells(b) == old);
        });
        test("Quoted TSV round-trips tabs, newlines, quotes, Unicode and empty trailing rows", () => {
            var b = Table(); b.Cells = [["a\tb", "line\r\nnext"], ["a\"b", "żółw😀"], ["", ""]];
            var restored = NoteTable.ParseTsv(NoteTable.ToTsv(b)); Check(restored.Count == 3 && restored.SelectMany(r => r).SequenceEqual(b.Cells.SelectMany(r => r)));
            b.Cells = [["value"], [""]]; Check(NoteTable.ParseTsv(NoteTable.ToTsv(b)).Count == 2);
        });
        test("TSV accepts external terminal CRLF and rectangularizes short rows", () => {
            var rows = NoteTable.ParseTsv("a\tb\r\nc\r\n"); Check(rows.Count == 2 && rows[1].SequenceEqual(["c", ""]));
        });
        test("Malformed and oversized TSV are rejected", () => {
            Throws<InvalidDataException>(() => NoteTable.ParseTsv("\"unclosed")); Throws<InvalidDataException>(() => NoteTable.ParseTsv("\"closed\"garbage"));
            Throws<InvalidDataException>(() => NoteTable.ParseTsv(new string('x', NoteTable.MaximumCellLength + 1)));
            Throws<InvalidDataException>(() => NoteTable.ParseTsv(string.Join('\t', Enumerable.Repeat("x", 51))));
            Throws<InvalidDataException>(() => NoteTable.ParseTsv(string.Join('\n', Enumerable.Repeat("x", 477))));
        });
        test("Pasting cells grows the rectangle and keeps unmodified neighbors", () => {
            var b = Table(); NoteTable.Paste(b, new(2, 1), "x\ty\nz\tw");
            Check(Cells(b) == "A|B|;one|two|;three|x|y;|z|w" && b.Height == 168);
        });
        test("Invalid table operations are atomic", () => {
            var b = Table(); var before = Cells(b); var height = b.Height;
            Throws<ArgumentOutOfRangeException>(() => NoteTable.SetCell(b, new(20, 0), "x"));
            Throws<InvalidDataException>(() => NoteTable.Paste(b, new(1, 1), "\"bad"));
            Throws<InvalidDataException>(() => NoteTable.SetCell(b, new(0, 0), new string('x', 100001)));
            Check(Cells(b) == before && b.Height == height);
        });
        test("Table size limits reject growth before assignment", () => {
            var b = Table(); b.Cells = Enumerable.Range(0, 476).Select(_ => Enumerable.Repeat("x", 50).ToList()).ToList();
            var before = b.Cells;
            Throws<InvalidOperationException>(() => NoteTable.InsertRow(b, 0)); Throws<InvalidOperationException>(() => NoteTable.InsertColumn(b, 0));
            Throws<InvalidOperationException>(() => NoteTable.Paste(b, new(475, 49), "a\tb")); Check(ReferenceEquals(before, b.Cells));
        });
        test("Cell editing and TSV paste are undoable single transactions", () => {
            var b = Table(); var p = new NotePage { Blocks = [b] }; var s = new EditorSession(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = [p] }] }] });
            s.EditPage(p.Id, "Paste", page => NoteTable.Paste(page.Blocks[0], new(1, 0), "a\tb\nc\td"));
            Check(s.UndoCount == 1 && s.SelectedPage!.Blocks[0].Cells[2][1] == "d"); s.Undo(); Check(Cells(s.SelectedPage!.Blocks[0]) == Cells(Table()));
            s.Redo(); Check(s.SelectedPage!.Blocks[0].Cells[2][1] == "d");
        });
        test("Sparse spatial queries inspect only nearby leaves", () => {
            var p = new NotePage { Blocks = Enumerable.Range(0, 10000).Select(i => new NoteBlock { X = i % 100 * 900, Y = i / 100 * 900, Width = 120, Height = 70 }).ToList() };
            var index = new PageContentIndex(p); var hits = new List<int>(); index.QueryBlocks(new(0, 0, 800, 600), hits);
            Check(hits.SequenceEqual([0]) && index.LastCandidatesTested < 128); Check(index.BlockIndex(p.Blocks[9876].Id) == 9876);
        });
        test("Spatial index matches conservative linear culling over randomized viewports", () => {
            var random = new Random(714);
            var p = new NotePage { Blocks = Enumerable.Range(0, 500).Select(_ => new NoteBlock { X = random.Next(10000), Y = random.Next(10000), Width = random.Next(24, 1200), Height = random.Next(12, 600), Tags = ["Important"] }).ToList() };
            p.Ink = Enumerable.Range(0, 100).Select(_ => new InkStroke { Points = [new(random.Next(10000), random.Next(10000)), new(random.Next(10000), random.Next(10000))] }).ToList();
            var index = new PageContentIndex(p); var hits = new List<int>();
            for (var i = 0; i < 100; i++)
            {
                var v = new NoteRect(random.Next(10000), random.Next(10000), 700, 500);
                index.QueryBlocks(v, hits);
                Check(hits.SequenceEqual(p.Blocks.Select((b, at) => (b, at)).Where(x => new NoteRect(x.b.X - 4, x.b.Y - 16, x.b.Width + 12 + x.b.Tags.Count * 44, x.b.Height + 24).Intersects(v)).Select(x => x.at)));
                index.QueryInk(v, hits); Check(hits.SequenceEqual(p.Ink.Select((s, at) => (s, at)).Where(x => InkGeometry.Bounds(x.s).Intersects(v)).Select(x => x.at)));
            }
        });
        test("Empty geometry index and invalid query rectangles are handled", () => {
            var index = new PageContentIndex(new NotePage()); var hits = new List<int> { 99 }; index.QueryInk(new(0, 0, 20, 20), hits); Check(hits.Count == 0);
            Throws<ArgumentOutOfRangeException>(() => index.QueryBlocks(new(float.NaN, 0, 2, 2), hits));
        });
    }
}
