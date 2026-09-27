using NoteSpace.Core;
using NoteSpace.Editor;

internal static class EditingRegressionTests
{
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static (EditorSession Session, string SectionId, string[] Ids) Outline(params int[] levels)
    {
        var section = new NoteSection { Pages = levels.Select((level, i) => new NotePage { Title = "P" + i, Level = level }).ToList() };
        var workspace = new Workspace { Notebooks = [new Notebook { Sections = [section] }] };
        workspace.Settings.SelectedPageId = section.Pages.FirstOrDefault()?.Id;
        return (new(workspace), section.Id, section.Pages.Select(p => p.Id).ToArray());
    }
    private static NoteBlock Text(string text = "abcdef") => new() { Text = text };
    private static string Order(EditorSession session, string sectionId) => string.Join(",", session.FindSection(sectionId)!.Pages.Select(p => p.Title + ":" + p.Level));
    private static void Canonical(NoteBlock block)
    {
        var end = 0;
        foreach (var mark in block.Marks) { Check(mark.Start >= end && mark.Length > 0 && mark.Start + mark.Length <= block.Text.Length); end = mark.Start + mark.Length; }
    }

    public static void Run(Action<string, Action> test)
    {
        test("Outline resolves parent and subtree ranges", () => {
            var (s, section, _) = Outline(0, 1, 2, 1, 0);
            var a = PageOutline.Build(s.FindSection(section)!.Pages);
            Check(a[0].EndIndex == 4 && a[1].EndIndex == 3 && a[2].ParentIndex == 1 && a[3].ParentIndex == 0 && a[4].ParentIndex == -1);
        });
        test("Legacy orphan indentation is queried without mutation", () => {
            var (s, section, _) = Outline(2, 2, 0, 2);
            var pages = s.FindSection(section)!.Pages; var a = PageOutline.Build(pages);
            Check(a.Select(x => x.Level).SequenceEqual([0, 1, 0, 1])); Check(pages.Select(p => p.Level).SequenceEqual([2, 2, 0, 2]));
        });
        test("Empty outline can be built and displayed", () => Check(PageOutline.Visible(PageOutline.Build([])).Count == 0));
        test("Collapsing a parent hides descendants and selects parent", () => {
            var (s, section, ids) = Outline(0, 1, 2, 0); s.SelectPage(ids[2]); s.SetPageCollapsed(ids[0], true);
            Check(s.SelectedPage!.Id == ids[0]); Check(PageOutline.Visible(PageOutline.Build(s.FindSection(section)!.Pages)).Count == 2);
            s.Undo(); Check(!s.FindPage(ids[0])!.IsCollapsed); s.Redo(); Check(s.FindPage(ids[0])!.IsCollapsed);
        });
        test("Search reveal traverses collapsed ancestors without changing them", () => {
            var (s, section, ids) = Outline(0, 1, 2, 0); s.SetPageCollapsed(ids[1], true); s.SetPageCollapsed(ids[0], true);
            var a = PageOutline.Build(s.FindSection(section)!.Pages);
            Check(PageOutline.Visible(a, ids[2]).Count == 4); Check(s.FindPage(ids[0])!.IsCollapsed && s.FindPage(ids[1])!.IsCollapsed);
        });
        test("Collapsed state survives backup round trip", () => {
            var (s, _, ids) = Outline(0, 1); s.SetPageCollapsed(ids[0], true);
            Check(DocumentJson.Deserialize(DocumentJson.Serialize(s.Document)).Notebooks[0].Sections[0].Pages[0].IsCollapsed);
        });
        test("Leaf collapse is a history-free no-op", () => { var (s, _, ids) = Outline(0); s.SetPageCollapsed(ids[0], true); Check(!s.CanUndo && s.Document.Revision == 0); });
        test("Add subpage appends inside its parent and expands it", () => {
            var (s, section, ids) = Outline(0, 1, 0); s.SetPageCollapsed(ids[0], true); var p = s.AddSubpage(ids[0], "Child");
            Check(Order(s, section) == "P0:0,P1:1,Child:1,P2:0"); Check(s.SelectedPage == p && !s.FindPage(ids[0])!.IsCollapsed);
        });
        test("Third subpage depth is rejected without mutation", () => {
            var (s, _, ids) = Outline(0, 1, 2); var before = DocumentJson.Serialize(s.Document);
            Throws<InvalidOperationException>(() => s.AddSubpage(ids[2])); Check(DocumentJson.Serialize(s.Document) == before);
        });
        test("Indent moves an entire subtree under its previous sibling", () => {
            var (s, section, ids) = Outline(0, 0, 1, 0); Check(s.IndentPage(ids[1])); Check(Order(s, section) == "P0:0,P1:1,P2:2,P3:0");
            s.Undo(); Check(Order(s, section) == "P0:0,P1:0,P2:1,P3:0"); s.Redo(); Check(s.FindPage(ids[2])!.Level == 2);
        });
        test("Indent refuses a first sibling or over-depth subtree", () => {
            var (s, _, ids) = Outline(0, 0, 1, 2); Check(!s.IndentPage(ids[0])); Check(!s.IndentPage(ids[1])); Check(!s.IndentPage(ids[2])); Check(!s.CanUndo);
        });
        test("Promote retains following siblings under their old parent", () => {
            var (s, section, ids) = Outline(0, 1, 2, 1, 0); Check(s.PromotePage(ids[1]));
            Check(Order(s, section) == "P0:0,P3:1,P1:0,P2:1,P4:0"); s.Undo(); Check(Order(s, section) == "P0:0,P1:1,P2:2,P3:1,P4:0");
        });
        test("Promote of a root is history-free", () => { var (s, _, ids) = Outline(0); Check(!s.PromotePage(ids[0]) && !s.CanUndo); });
        test("Sibling reorder keeps nested groups together", () => {
            var (s, section, ids) = Outline(0, 1, 2, 0, 1); Check(s.MovePageSibling(ids[0], 1));
            Check(Order(s, section) == "P3:0,P4:1,P0:0,P1:1,P2:2"); Check(s.MovePageSibling(ids[0], -1)); Check(Order(s, section) == "P0:0,P1:1,P2:2,P3:0,P4:1");
        });
        test("Sibling reorder cannot cross a parent boundary", () => {
            var (s, _, ids) = Outline(0, 1, 0, 1); Check(!s.MovePageSibling(ids[1], 1)); Check(!s.MovePageSibling(ids[3], -1));
            Throws<ArgumentOutOfRangeException>(() => s.MovePageSibling(ids[0], 2)); Check(!s.CanUndo);
        });
        test("Moving sections carries descendants and normalizes root depth", () => {
            var (s, section, ids) = Outline(0, 1, 2, 0); var target = s.AddSection(s.Document.Notebooks[0].Id, "Target");
            s.MovePage(ids[1], target.Id, 0);
            Check(Order(s, section) == "P0:0,P3:0"); Check(target.Pages[0].Id == ids[1] && target.Pages[0].Level == 0 && target.Pages[1].Id == ids[2] && target.Pages[1].Level == 1);
        });
        test("A move cannot split an unrelated destination group", () => {
            var (s, section, ids) = Outline(0, 1, 0); s.MovePage(ids[2], section, 1); Check(Order(s, section) == "P0:0,P1:1,P2:0");
        });
        test("Duplicate does not adopt original subpages", () => {
            var (s, section, ids) = Outline(0, 1, 2, 0); s.DuplicatePage(ids[0]); Check(Order(s, section) == "P0:0,P1:1,P2:2,P0 (copy):0,P3:0");
        });
        test("Delete promotes descendants and recovery restores only the page", () => {
            var (s, section, ids) = Outline(0, 1, 2, 1, 0); s.DeletePage(ids[1]);
            Check(Order(s, section) == "P0:0,P2:1,P3:1,P4:0"); s.RestorePage(ids[1]); Check(Order(s, section) == "P0:0,P2:1,P3:1,P4:0,P1:0");
        });
        test("Deleting the final page leaves a valid empty section", () => {
            var (s, section, ids) = Outline(0); s.DeletePage(ids[0]); Check(s.SelectedPage is null && s.FindSection(section)!.Pages.Count == 0); s.Undo(); Check(s.SelectedPage is not null);
        });
        test("Outline randomized edits preserve IDs, depths and undo snapshots", () => {
            var (s, section, ids) = Outline(0, 1, 2, 1, 0, 1, 0, 0); var random = new Random(1749);
            for (var i = 0; i < 160; i++)
            {
                var id = ids[random.Next(ids.Length)]; var old = Order(s, section); var revision = s.Document.Revision;
                switch (random.Next(4)) { case 0: s.IndentPage(id); break; case 1: s.PromotePage(id); break; case 2: s.MovePageSibling(id, random.Next(2) == 0 ? -1 : 1); break; default: s.SetPageCollapsed(id, random.Next(2) == 0); break; }
                var pages = s.FindSection(section)!.Pages; Check(pages.Select(p => p.Id).Order().SequenceEqual(ids.Order())); Check(pages[0].Level == 0);
                for (var j = 1; j < pages.Count; j++) Check(pages[j].Level <= pages[j - 1].Level + 1);
                if (s.Document.Revision != revision) { var after = Order(s, section); s.Undo(); Check(Order(s, section) == old); s.Redo(); Check(Order(s, section) == after); }
                DocumentJson.Validate(s.Document);
            }
        });
        test("Range formatting preserves mixed attributes", () => {
            var b = Text(); RichText.Apply(b, 0, 3, f => f.Bold = true); RichText.Apply(b, 3, 3, f => f.Italic = true); RichText.Apply(b, 1, 4, f => f.Underline = true);
            Check(RichText.At(b, 1).Bold && RichText.At(b, 1).Underline && !RichText.At(b, 1).Italic);
            Check(RichText.At(b, 4).Italic && RichText.At(b, 4).Underline && !RichText.At(b, 4).Bold); Check(!RichText.At(b, 0).Underline && !RichText.At(b, 5).Underline); Canonical(b);
        });
        test("Whole-container formatting updates existing marked runs", () => {
            var b = Text(); RichText.Apply(b, 0, 3, f => f.Bold = true); RichText.Apply(b, 0, 0, f => f.Color = 0xFF336699);
            Check(b.Format.Color == 0xFF336699 && RichText.At(b, 1).Color == 0xFF336699 && RichText.At(b, 1).Bold);
        });
        test("Repeated formatting remains canonical instead of accumulating marks", () => {
            var b = Text(); for (var i = 0; i < 1000; i++) RichText.Apply(b, 1, 4, f => f.Bold = true);
            Check(b.Marks.Count == 1); RichText.Apply(b, 1, 4, f => f.Bold = false); Check(b.Marks.Count == 0);
        });
        test("Toolbar toggle uses a uniform mixed-selection state", () => {
            var b = Text(); RichText.Apply(b, 0, 3, f => f.Bold = true); var enabled = !RichText.AllHave(b, 0, 6, f => f.Bold);
            RichText.Apply(b, 0, 6, f => f.Bold = enabled); Check(RichText.AllHave(b, 0, 6, f => f.Bold));
            enabled = !RichText.AllHave(b, 0, 6, f => f.Bold); RichText.Apply(b, 0, 6, f => f.Bold = enabled); Check(!RichText.At(b, 0).Bold && !RichText.At(b, 5).Bold);
        });
        test("Formatting preserves and explicitly removes hyperlinks", () => {
            var b = Text(); RichText.Apply(b, 0, 6, f => f.Underline = true, "https://example.org"); RichText.Apply(b, 1, 4, f => f.Bold = true);
            Check(RichText.GetRuns(b).All(r => r.Link == "https://example.org")); RichText.Apply(b, 1, 4, _ => { }, "");
            Check(RichText.GetRuns(b).Single(r => r.Start == 1).Link is null); Check(b.Marks.First().Link == "https://example.org");
        });
        test("Legacy overlap resolves with last mark winning", () => {
            var b = Text(); b.Marks = [new() { Start = 0, Length = 6, Format = new() { Bold = true } }, new() { Start = 2, Length = 2, Format = new() { Italic = true } }];
            var runs = RichText.GetRuns(b); Check(runs.Count == 3 && runs[1].Format.Italic && !runs[1].Format.Bold);
            RichText.Apply(b, 0, 6, f => f.Strike = true); Canonical(b); Check(RichText.At(b, 2).Italic && !RichText.At(b, 2).Bold);
        });
        test("Resolved runs are detached from document formatting", () => { var b = Text(); var runs = RichText.GetRuns(b); runs[0].Format.Bold = true; Check(!b.Format.Bold); });
        test("Formatting callback failure does not partially modify a block", () => {
            var b = Text(); RichText.Apply(b, 3, 3, f => f.Italic = true); var count = 0;
            Throws<InvalidOperationException>(() => RichText.Apply(b, 0, 6, f => { f.Bold = true; if (++count == 2) throw new InvalidOperationException(); }));
            Check(!b.Format.Bold && !RichText.At(b, 0).Bold && RichText.At(b, 4).Italic);
        });
        test("Range API rejects split surrogate pairs", () => {
            var b = Text("a😀b"); Throws<ArgumentException>(() => RichText.Apply(b, 2, 1, f => f.Bold = true)); Throws<ArgumentException>(() => RichText.ReplaceRange(b, 1, 1, "x")); Check(b.Text == "a😀b");
        });
        test("Whole-value emoji edits keep styles after the edited pair", () => {
            var b = Text("a😀b"); RichText.Apply(b, 3, 1, f => f.Bold = true); RichText.Replace(b, "a😃b"); Check(b.Text == "a😃b" && RichText.At(b, 3).Bold); Canonical(b);
        });
        test("Typing within a styled range inherits its format", () => {
            var b = Text(); RichText.Apply(b, 1, 4, f => f.Bold = true); RichText.ReplaceRange(b, 3, 0, "XY"); Check(b.Text == "abcXYdef" && RichText.At(b, 3).Bold && RichText.At(b, 6).Bold); Canonical(b);
        });
        test("Typing at a hyperlink edge does not extend the link", () => {
            var b = Text("abc"); RichText.Apply(b, 0, 3, _ => { }, "https://example.org"); RichText.ReplaceRange(b, 3, 0, "d");
            Check(b.Marks.Single().Length == 3 && RichText.GetRuns(b).Last().Link is null);
        });
        test("Typing inside a hyperlink preserves the link", () => {
            var b = Text("abc"); RichText.Apply(b, 0, 3, _ => { }, "https://example.org"); RichText.ReplaceRange(b, 1, 0, "d"); Check(b.Marks.Single().Length == 4 && b.Marks[0].Link is not null);
        });
        test("Replace All preserves formatting between disjoint matches", () => {
            var b = Text("foo KEEP foo"); RichText.Apply(b, 4, 4, f => f.Bold = true);
            Check(RichText.ReplaceAll(b, "foo", "x") == 2 && b.Text == "x KEEP x"); Check(RichText.At(b, 2).Bold && RichText.At(b, 5).Bold && !RichText.At(b, 0).Bold); Canonical(b);
        });
        test("Replacement inherits each occurrence's own format", () => {
            var b = Text("foo foo"); RichText.Apply(b, 4, 3, f => f.Italic = true); RichText.ReplaceAll(b, "foo", "long");
            Check(b.Text == "long long" && !RichText.At(b, 0).Italic && RichText.At(b, 5).Italic);
        });
        test("Adjacent replacements and deletions use non-overlapping matches", () => {
            var b = Text("aaaa"); Check(RichText.ReplaceAll(b, "aa", "b") == 2 && b.Text == "bb"); Check(RichText.ReplaceAll(b, "b", "") == 2 && b.Text == "" && b.Marks.Count == 0);
        });
        test("Case-insensitive replacement uses ordinal semantics", () => { var b = Text("Foo fOO food"); Check(RichText.ReplaceAll(b, "foo", "x") == 3 && b.Text == "x x xd"); });
        test("Unmatched replacement leaves styles untouched", () => { var b = Text(); RichText.Apply(b, 0, 3, f => f.Bold = true); var marks = b.Marks; Check(RichText.ReplaceAll(b, "missing", "x") == 0 && ReferenceEquals(marks, b.Marks)); });
        test("Empty search and unsupported comparison are rejected", () => {
            var b = Text(); Throws<ArgumentException>(() => RichText.ReplaceAll(b, "", "x")); Throws<ArgumentOutOfRangeException>(() => RichText.ReplaceAll(b, "a", "x", StringComparison.CurrentCulture));
        });
        test("Oversized replacements leave text and formatting unchanged", () => {
            var b = Text(); RichText.Apply(b, 0, 3, f => f.Bold = true); Throws<InvalidDataException>(() => RichText.ReplaceRange(b, 1, 0, new string('x', 2 * 1024 * 1024)));
            Check(b.Text == "abcdef" && b.Marks.Single().Length == 3);
        });
        test("Empty text uses base format and accepts insertion", () => { var b = Text(""); RichText.Apply(b, 0, 0, f => f.Bold = true); RichText.ReplaceRange(b, 0, 0, "x"); Check(b.Text == "x" && RichText.At(b, 0).Bold); });
        test("Random range formatting agrees with a per-character reference", () => {
            var random = new Random(771); var b = Text(new string('x', 60)); var bold = new bool[60]; var italic = new bool[60];
            for (var i = 0; i < 200; i++)
            {
                var start = random.Next(60); var length = random.Next(1, 61 - start); var value = random.Next(2) == 0; var useBold = random.Next(2) == 0;
                RichText.Apply(b, start, length, f => { if (useBold) f.Bold = value; else f.Italic = value; });
                for (var j = start; j < start + length; j++) if (useBold) bold[j] = value; else italic[j] = value;
                for (var j = 0; j < 60; j++) Check(RichText.At(b, j).Bold == bold[j] && RichText.At(b, j).Italic == italic[j]); Canonical(b);
            }
        });
    }
}
