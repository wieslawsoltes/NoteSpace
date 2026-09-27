using NoteSpace.Core;
using NoteSpace.Editor;

internal static class SearchTests
{
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static EditorSession Fixture()
    {
        var w = new Workspace { Notebooks = [
            new Notebook { Id = "book-a", Title = "A", Sections = [new NoteSection { Id = "section-a", Title = "Alpha", Pages = [
                new NotePage { Id = "page-a", Title = "Project title", Blocks = [
                    new NoteBlock { Id = "text-a", Text = "Plan planning PLAN", Tags = ["Important"] },
                    new NoteBlock { Id = "todo-open", Kind = BlockKind.Checklist, Text = "Plan open" },
                    new NoteBlock { Id = "todo-done", Kind = BlockKind.Checklist, Text = "Plan complete", Checked = true },
                    new NoteBlock { Id = "table-a", Kind = BlockKind.Table, Cells = [["Owner", "Status"], ["Ada", "Plan"]] }
                ] }, new NotePage { Id = "page-b", Title = "Other", Blocks = [new NoteBlock { Id = "text-b", Text = "Plan other" }] }
            ] }] },
            new Notebook { Id = "book-b", Title = "B", Sections = [new NoteSection { Id = "section-b", Title = "Beta", Pages = [
                new NotePage { Id = "page-c", Title = "Remote", Blocks = [new NoteBlock { Id = "text-c", Text = "Plan remote" }] }
            ] }] }
        ] };
        w.Settings.SelectedPageId = "page-a"; return new(w);
    }
    public static void Run(Action<string, Action> test)
    {
        test("Scoped search selects only the requested notebook section or page", () => {
            var s = Fixture(); Check(s.Search(new NoteSearchQuery("Plan")).Count() == 6);
            Check(s.Search(new NoteSearchQuery("Plan") { Scope = NoteSearchScope.Notebook, ScopeId = "book-b" }).Single().PageId == "page-c");
            Check(s.Search(new NoteSearchQuery("Plan") { Scope = NoteSearchScope.Section, ScopeId = "section-a" }).Count() == 5);
            Check(s.Search(new NoteSearchQuery("Plan") { Scope = NoteSearchScope.Page, ScopeId = "page-a" }).Count() == 4);
        });
        test("Missing search scope never widens to all notebooks", () => {
            var s = Fixture(); Check(!s.Search(new NoteSearchQuery("Plan") { Scope = NoteSearchScope.Page, ScopeId = "removed" }).Any());
            Throws<ArgumentException>(() => s.Search(new NoteSearchQuery("Plan") { Scope = NoteSearchScope.Page }).ToList());
        });
        test("Exact text offsets and table cell addresses are returned", () => {
            var s = Fixture(); var hit = s.Search(new NoteSearchQuery("planning")).Single(); Check(hit.BlockId == "text-a" && hit.Start == 5 && hit.Length == 8);
            hit = s.Search(new NoteSearchQuery("Ada")).Single(); Check(hit.Cell == new TableCellAddress(1, 0) && hit.Start == 0 && hit.Location == "A / Alpha");
        });
        test("Title search is separately selectable", () => {
            var s = Fixture(); Check(s.Search(new NoteSearchQuery("title")).Single().BlockId is null);
            Check(!s.Search(new NoteSearchQuery("title") { IncludeTitles = false }).Any());
        });
        test("Whole-word search excludes longer words and underscore identifiers", () => {
            var hits = LiteralTextSearch.Find("plan planning plan_1 _plan (plan) PLAN", "plan", wholeWord: true).ToArray(); Check(hits.Length == 3);
        });
        test("Whole-word search recognizes Unicode combining and supplementary letters", () => {
            Check(LiteralTextSearch.Find("a a\u0301 \U00010400a a\U00010400 a_", "a", wholeWord: true).Count() == 1);
            Check(LiteralTextSearch.Find("żółć żółćmi", "żółć", wholeWord: true).Count() == 1);
        });
        test("Case matching is ordinal and can distinguish variants", () => {
            Check(LiteralTextSearch.Find("Plan PLAN plan", "Plan", true).Count() == 1);
            Check(LiteralTextSearch.Find("Plan PLAN plan", "Plan", false).Count() == 3);
        });
        test("Search never returns a range splitting an emoji surrogate pair", () => {
            Check(!LiteralTextSearch.Find("😀", "\ud83d").Any()); Check(LiteralTextSearch.Find("😀😀", "😀").Count() == 2);
        });
        test("Tag-only search does not match body text", () => {
            var s = Fixture(); Check(s.Search(new NoteSearchQuery("Important") { Filter = NoteSearchFilter.TagsOnly }).Single().Start == -1);
            Check(!s.Search(new NoteSearchQuery("Plan") { Filter = NoteSearchFilter.TagsOnly }).Any());
        });
        test("To-do filters support empty query and distinguish checked items", () => {
            var s = Fixture(); Check(s.Search(new NoteSearchQuery("") { Filter = NoteSearchFilter.OpenToDos }).Single().BlockId == "todo-open");
            Check(s.Search(new NoteSearchQuery("Plan") { Filter = NoteSearchFilter.CompletedToDos }).Single().BlockId == "todo-done");
            Check(!s.Search(new NoteSearchQuery("")).Any());
        });
        test("Result limits stop the stream without mutating the workspace", () => {
            var s = Fixture(); var before = DocumentJson.Serialize(s.Document); Check(s.Search(new NoteSearchQuery("Plan") { MaximumResults = 2 }).Count() == 2);
            Check(before == DocumentJson.Serialize(s.Document) && !s.CanUndo);
            Throws<ArgumentOutOfRangeException>(() => s.Search(new NoteSearchQuery("Plan") { MaximumResults = 0 }));
        });
        test("Search cancellation is honored", () => {
            var s = Fixture(); using var source = new CancellationTokenSource(); source.Cancel();
            Throws<OperationCanceledException>(() => s.Search(new NoteSearchQuery("Plan"), source.Token).ToList());
        });
        test("Search sees page edits undo and redo without stale cache entries", () => {
            var s = Fixture(); s.UpdateText("page-c", "text-c", "unique"); Check(s.Search(new NoteSearchQuery("unique")).Count() == 1);
            s.Undo(); Check(!s.Search(new NoteSearchQuery("unique")).Any()); s.Redo(); Check(s.Search(new NoteSearchQuery("unique")).Count() == 1);
        });
        test("Scoped whole-word replacement preserves other pages and titles", () => {
            var s = Fixture(); var result = s.ReplaceAll(new("Plan") { Scope = NoteSearchScope.Page, ScopeId = "page-a", WholeWord = true }, "Done");
            Check(result == new NoteReplaceResult(5, 4, 1)); Check(s.FindPage("page-a")!.Blocks[0].Text == "Done planning Done");
            Check(s.FindPage("page-b")!.Blocks[0].Text == "Plan other" && s.FindPage("page-a")!.Title == "Project title");
            Check(s.FindPage("page-a")!.Blocks[0].Tags.Single() == "Important");
        });
        test("Replacement is one chronological undo action across pages and table cells", () => {
            var s = Fixture(); var before = DocumentJson.Serialize(s.Document); var result = s.ReplaceAll(new("Plan") { WholeWord = true }, "Reviewed");
            Check(result.Matches == 7 && result.ChangedPages == 3 && s.UndoCount == 1);
            s.Undo(); var restored = DocumentJson.Deserialize(before); restored.Revision = s.Document.Revision; Check(DocumentJson.Serialize(restored) == DocumentJson.Serialize(s.Document));
            s.Redo(); Check(s.FindPage("page-a")!.Blocks[3].Cells[1][1] == "Reviewed");
        });
        test("Replacement preserves styles between disjoint whole-word matches", () => {
            var b = new NoteBlock { Text = "Plan KEEP planning PLAN" }; RichText.Apply(b, 5, 4, f => f.Bold = true);
            Check(RichText.ReplaceAll(b, "Plan", "x", false, true) == 2); Check(b.Text == "x KEEP planning x" && RichText.At(b, 2).Bold && !RichText.At(b, 0).Bold);
        });
        test("Case-insensitive replacement can normalize casing to the search spelling", () => {
            var b = new NoteBlock { Text = "PLAN plan Plan" }; RichText.ReplaceAll(b, "plan", "plan", false, true); Check(b.Text == "plan plan plan");
        });
        test("Identical replacement does not create history or change timestamps", () => {
            var s = Fixture(); var before = DocumentJson.Serialize(s.Document); var result = s.ReplaceAll(new("Plan") { MatchCase = true, WholeWord = true }, "Plan");
            Check(result.Matches > 0 && result.ChangedContainers == 0 && !s.CanUndo && DocumentJson.Serialize(s.Document) == before);
        });
        test("Replacement can restrict edits to open to-do items", () => {
            var s = Fixture(); var result = s.ReplaceAll(new("Plan") { Filter = NoteSearchFilter.OpenToDos }, "Complete");
            Check(result.ChangedContainers == 1 && s.FindPage("page-a")!.Blocks[1].Text == "Complete open" && s.FindPage("page-a")!.Blocks[2].Text == "Plan complete");
        });
        test("Replacement rollback includes earlier edits when a table cell exceeds its limit", () => {
            var s = Fixture(); var before = DocumentJson.Serialize(s.Document);
            Throws<InvalidDataException>(() => s.ReplaceAll(new("Plan"), new string('x', NoteTable.MaximumCellLength + 1)));
            Check(DocumentJson.Serialize(s.Document) == before && !s.CanUndo);
        });
        test("Canceled replacement retains redo and does not partially commit", () => {
            var s = Fixture(); s.RenamePage("page-a", "x"); s.Undo(); var before = DocumentJson.Serialize(s.Document);
            using var source = new CancellationTokenSource(); source.Cancel();
            Throws<OperationCanceledException>(() => s.ReplaceAll(new("Plan"), "x", source.Token)); Check(s.CanRedo && DocumentJson.Serialize(s.Document) == before);
        });
        test("Reflow callback runs inside the replacement transaction", () => {
            var s = Fixture(); var before = DocumentJson.Serialize(s.Document);
            Throws<InvalidOperationException>(() => s.ReplaceAll(new("Plan"), "x", reflow: _ => throw new InvalidOperationException()));
            Check(DocumentJson.Serialize(s.Document) == before && !s.CanUndo);
            s.ReplaceAll(new("Plan"), "x", reflow: block => block.Height = 450); Check(s.FindPage("page-a")!.Blocks[0].Height == 450);
        });
        test("Replacement processes the entire scope even with a one-result search limit", () => {
            var s = Fixture(); var result = s.ReplaceAll(new("Plan") { WholeWord = true, MaximumResults = 1 }, "x"); Check(result.Matches == 7);
        });
        test("Invalid replacements are rejected before making document changes", () => {
            var s = Fixture(); Throws<ArgumentException>(() => s.ReplaceAll(new(""), "x"));
            Throws<ArgumentException>(() => s.ReplaceAll(new("Plan") { Filter = NoteSearchFilter.TagsOnly }, "x")); Check(!s.CanUndo);
        });
    }
}
