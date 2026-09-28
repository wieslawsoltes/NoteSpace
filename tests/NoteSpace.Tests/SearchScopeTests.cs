using NoteSpace.Core;
using NoteSpace.Editor;

internal static class SearchScopeTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Search scope assertion failed."); }
    public static void Run(Action<string, Action> test)
    {
        static EditorSession Create() => new(new Workspace { Notebooks = [
            new Notebook { Id = "a", Sections = [new NoteSection { Id = "s", Pages = [new NotePage { Id = "p", Title = "Needle", Blocks = [new NoteBlock { Text = "Needle note" }] }] }] },
            new Notebook { Id = "b", Sections = [new NoteSection { Id = "t", Pages = [new NotePage { Id = "q", Title = "Needle", Blocks = [new NoteBlock { Text = "Needle other" }] }] }] }
        ] });
        test("Indexed page scope matches filtered full results in document order", () => {
            var s = Create(); var all = s.Search(new NoteSearchQuery("Needle")).ToArray();
            Check(s.Search(new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Page, ScopeId = "p" }).SequenceEqual(all.Where(h => h.PageId == "p")));
        });
        test("Indexed section and notebook scopes retain complete matching order", () => {
            var s = Create(); var all = s.Search(new NoteSearchQuery("Needle")).ToArray();
            Check(s.Search(new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Section, ScopeId = "t" }).SequenceEqual(all.Where(h => h.SectionId == "t")));
            Check(s.Search(new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Notebook, ScopeId = "b" }).SequenceEqual(all.Where(h => h.NotebookId == "b")));
        });
        test("Scope lookup follows cross-section moves and their undo", () => {
            var s = Create(); _ = s.Search(new NoteSearchQuery("Needle")).ToArray(); s.MovePage("p", "t", 0);
            var q = new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Page, ScopeId = "p" };
            Check(s.Search(q).All(h => h.SectionId == "t" && h.NotebookId == "b"));
            s.Undo(); Check(s.Search(q).All(h => h.SectionId == "s" && h.NotebookId == "a"));
        });
        test("Search inside a transaction observes newly inserted pages", () => {
            var s = Create(); _ = s.Pages.ToArray();
            s.Execute("Insert", w => {
                w.Notebooks[1].Sections[0].Pages.Add(new NotePage { Id = "new", Title = "Needle" });
                Check(s.Search(new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Page, ScopeId = "new" }).Single().NotebookId == "b");
            });
        });
        test("Deleted page and empty section scopes return no stale results", () => {
            var s = Create(); _ = s.Pages.ToArray(); s.DeletePage("p");
            Check(!s.Search(new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Page, ScopeId = "p" }).Any());
            Check(!s.Search(new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Section, ScopeId = "s" }).Any());
            s.RestorePage("p"); Check(s.Search(new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Section, ScopeId = "s" }).Count() == 2);
        });
        test("Canceled empty and removed-scope queries honor cancellation", () => {
            var s = Create(); using var cts = new CancellationTokenSource(); cts.Cancel();
            foreach (var q in new[] { new NoteSearchQuery(""), new NoteSearchQuery("Needle") { Scope = NoteSearchScope.Page, ScopeId = "removed" } })
            {
                var canceled = false; try { _ = s.Search(q, cts.Token).ToArray(); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled);
            }
        });
    }
}
