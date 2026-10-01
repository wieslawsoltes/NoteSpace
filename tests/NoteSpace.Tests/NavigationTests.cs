using NoteSpace.Core;
using NoteSpace.Editor;

internal static class NavigationTests
{
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static PageVisit Visit(string id, float y = 0, float zoom = 1) => new(id, new(0, y, zoom));
    private static Workspace WorkspaceWith(params NotePage[] pages) => new() { Notebooks = [new() { Sections = [new() { Pages = pages.ToList() }] }] };
    public static void Run(Action<string, Action> test)
    {
        test("Navigation defaults read old backups without changing page content", () => {
            var w = DocumentJson.Deserialize("{\"schemaVersion\":1}");
            Check(w.Settings.Navigation.NotebookWidth == 204 && w.Settings.Navigation.PageSort == PageSortMode.Manual && !w.Settings.Navigation.ShowPagePreviews);
        });
        test("Navigation preferences round trip and clone independently", () => {
            var w = WorkspaceWith(new NotePage()); var n = w.Settings.Navigation;
            n.NotebookWidth = 280; n.PageWidth = 360; n.SearchWidth = 450; n.PageSort = PageSortMode.TitleDescending; n.ShowPagePreviews = true; n.ShowPageDates = true;
            var copy = DocumentJson.Clone(w); n.PageWidth = 140;
            Check(copy.Settings.Navigation.PageWidth == 360 && copy.Settings.Navigation.ShowPageDates && copy.Settings.Navigation.PageSort == PageSortMode.TitleDescending);
        });
        test("Invalid pane sizes sort codes and null navigation preferences are rejected", () => {
            foreach (var value in new[] { double.NaN, double.PositiveInfinity, 139, 481 })
            { var w = WorkspaceWith(); w.Settings.Navigation.PageWidth = value; Throws<InvalidDataException>(() => DocumentJson.Validate(w)); }
            var bad = WorkspaceWith(); bad.Settings.Navigation.PageSort = (PageSortMode)99; Throws<InvalidDataException>(() => DocumentJson.Validate(bad));
            bad.Settings.Navigation = null!; Throws<InvalidDataException>(() => DocumentJson.Validate(bad));
        });
        test("Page sort preserves hierarchical subtrees and the saved manual order", () => {
            var pages = new[] { new NotePage { Title = "Zulu" }, new NotePage { Title = "z child", Level = 1 }, new NotePage { Title = "b child", Level = 1 }, new NotePage { Title = "nested", Level = 2 }, new NotePage { Title = "Alpha" } };
            var w = WorkspaceWith(pages); var before = DocumentJson.Serialize(w);
            var view = PagePresentation.Build(pages, PageSortMode.TitleAscending);
            Check(view.Select(e => e.Page.Title).SequenceEqual(["Alpha", "Zulu", "b child", "nested", "z child"]));
            Check(view[3].ParentIndex == 2 && view[1].EndIndex == 5 && view[2].Level == 1);
            Check(DocumentJson.Serialize(w) == before);
        });
        test("Descending titles sort siblings without detaching children", () => {
            var pages = new[] { new NotePage { Title = "Alpha" }, new NotePage { Title = "child", Level = 1 }, new NotePage { Title = "Zulu" } };
            var view = PagePresentation.Build(pages, PageSortMode.TitleDescending);
            Check(view[0].Page == pages[2] && view[1].Page == pages[0] && view[2].ParentIndex == 1);
        });
        test("Sorted ties are stable in manual sibling order", () => {
            var pages = Enumerable.Range(0, 10).Select(_ => new NotePage { Title = "Same" }).ToArray();
            Check(PagePresentation.Build(pages, PageSortMode.TitleAscending).Select(e => e.Page.Id).SequenceEqual(pages.Select(p => p.Id)));
        });
        test("Date sorting compares created and modified independently", () => {
            var old = DateTimeOffset.UnixEpoch; var newer = old.AddDays(2);
            var a = new NotePage { Created = newer, Modified = old }; var b = new NotePage { Created = old, Modified = newer };
            Check(PagePresentation.Build([a, b], PageSortMode.CreatedNewest)[0].Page == a);
            Check(PagePresentation.Build([a, b], PageSortMode.ModifiedNewest)[0].Page == b);
        });
        test("Sorted collapse and selection reveal use sorted subtree ranges", () => {
            var a = new NotePage { Title = "Z", IsCollapsed = true }; var child = new NotePage { Title = "child", Level = 1 };
            var b = new NotePage { Title = "A" }; var view = PagePresentation.Build([a, child, b], PageSortMode.TitleAscending);
            Check(PageOutline.Visible(view).Count == 2 && PageOutline.Visible(view, child.Id).Count == 3 && a.IsCollapsed);
        });
        test("Sorting legacy orphan levels never normalizes the persisted document", () => {
            var pages = new[] { new NotePage { Title = "Z", Level = 2 }, new NotePage { Title = "A", Level = 2 } };
            var view = PagePresentation.Build(pages, PageSortMode.TitleAscending);
            Check(view[0].Level == 0 && view[1].Level == 1 && pages.All(p => p.Level == 2));
        });
        test("Empty page presentation and invalid sort values are explicit", () => {
            Check(PagePresentation.Build([], PageSortMode.ModifiedNewest).Count == 0);
            Throws<ArgumentOutOfRangeException>(() => PagePresentation.Build([], (PageSortMode)999));
        });
        test("All randomized sorted subtrees retain their original parents", () => {
            var random = new Random(5103);
            for (var round = 0; round < 40; round++)
            {
                var pages = Enumerable.Range(0, 150).Select(i => new NotePage { Title = random.Next(20).ToString(), Level = random.Next(3) }).ToArray();
                var original = PageOutline.Build(pages); var parents = original.ToDictionary(x => x.Page.Id, x => x.ParentIndex < 0 ? null : original[x.ParentIndex].Page.Id);
                foreach (var mode in Enum.GetValues<PageSortMode>())
                {
                    var sorted = PagePresentation.Build(pages, mode); Check(sorted.Count == pages.Length && sorted.Select(e => e.Page.Id).Distinct().Count() == pages.Length);
                    foreach (var entry in sorted) Check(parents[entry.Page.Id] == (entry.ParentIndex < 0 ? null : sorted[entry.ParentIndex].Page.Id));
                }
            }
        });
        test("Preview collapses whitespace and is bounded without changing text", () => {
            var b = new NoteBlock { Text = "  hello\r\n\tworld  " + new string('z', 20000) }; var page = new NotePage { Blocks = [b] };
            var preview = PagePresentation.Preview(page, 30); Check(preview.StartsWith("hello world ") && preview.EndsWith('…') && preview.Length <= 30 && b.Text.Length > 20000);
        });
        test("Preview never splits emoji or combining sequences", () => {
            var page = new NotePage { Blocks = [new NoteBlock { Text = string.Concat(Enumerable.Repeat("😀e\u0301", 40)) }] };
            var preview = PagePresentation.Preview(page, 12); Check(preview.Length <= 12 && preview.EndsWith('…'));
            var body = preview[..^1]; Check(body.Length % 4 == 0 || body.Length % 4 == 2); Check(!char.IsHighSurrogate(body[^1]));
        });
        test("Preview supports tables attachments empty pages and drawing-only pages", () => {
            Check(PagePresentation.Preview(new NotePage()) == "Empty page");
            Check(PagePresentation.Preview(new NotePage { Ink = [new InkStroke()] }).Contains("Drawing"));
            Check(PagePresentation.Preview(new NotePage { Blocks = [new NoteBlock { Kind = BlockKind.Table, Cells = [["", "Schedule"]] }] }) == "Schedule");
            Check(PagePresentation.Preview(new NotePage { Blocks = [new NoteBlock { Kind = BlockKind.Attachment, FileName = "manual.pdf", Data = new byte[2000000] }] }) == "manual.pdf");
        });
        test("Preview bounds scanning even for megabytes of leading whitespace", () => {
            var page = new NotePage { Blocks = [new NoteBlock { Text = new string(' ', 2 * 1024 * 1024) }] };
            PagePresentation.Preview(page); var before = GC.GetAllocatedBytesForCurrentThread();
            Check(PagePresentation.Preview(page) == "Page content"); Check(GC.GetAllocatedBytesForCurrentThread() - before < 300000);
        });
        test("Browsing history preserves viewports independently of document undo", () => {
            var h = new PageNavigationHistory(); h.Record(Visit("a", 10)); h.Record(Visit("b", 20)); h.UpdateCurrent(Visit("b", 350, 1.5f));
            Check(h.Move(-1, _ => true) == Visit("a", 10)); Check(h.Move(1, _ => true) == Visit("b", 350, 1.5f));
        });
        test("Same-page navigation updates rather than duplicates history", () => {
            var h = new PageNavigationHistory(); h.Record(Visit("a")); h.Record(Visit("a", 200)); Check(h.Count == 1 && h.Current!.Viewport.OffsetY == 200);
        });
        test("Navigation after going back truncates the forward branch", () => {
            var h = new PageNavigationHistory(); foreach (var id in new[] { "a", "b", "c" }) h.Record(Visit(id));
            h.Move(-1, _ => true); h.Record(Visit("d")); Check(h.Peek(1, _ => true) is null && h.Count == 3 && h.Move(-1, _ => true)!.PageId == "b");
        });
        test("History skips deleted pages without reintroducing their content", () => {
            var h = new PageNavigationHistory(); foreach (var id in new[] { "a", "b", "c" }) h.Record(Visit(id));
            Check(h.Peek(-1, id => id != "b")!.PageId == "a" && h.Current!.PageId == "c");
            Check(h.Move(-1, id => id != "b")!.PageId == "a" && h.Move(1, id => id != "b")!.PageId == "c");
        });
        test("Blocked history navigation and viewport updates leave the cursor alone", () => {
            var h = new PageNavigationHistory(); h.Record(Visit("a")); h.Record(Visit("b"));
            Check(h.Move(-1, _ => false) is null); h.UpdateCurrent(Visit("a", 999)); Check(h.Current == Visit("b"));
        });
        test("History capacity is bounded and clear releases all visits", () => {
            var h = new PageNavigationHistory(3); foreach (var id in new[] { "a", "b", "c", "d" }) h.Record(Visit(id));
            Check(h.Count == 3 && h.Move(-1, _ => true)!.PageId == "c" && h.Move(-1, _ => true)!.PageId == "b" && h.Move(-1, _ => true) is null);
            h.Clear(); Check(h.Current is null && h.Count == 0);
        });
        test("History validates inputs before discarding forward navigation", () => {
            var h = new PageNavigationHistory(); h.Record(Visit("a")); h.Record(Visit("b")); h.Move(-1, _ => true);
            Throws<ArgumentOutOfRangeException>(() => h.Record(Visit("c", float.NaN))); Check(h.Peek(1, _ => true)!.PageId == "b");
            Throws<ArgumentOutOfRangeException>(() => h.Move(0, _ => true)); Throws<ArgumentOutOfRangeException>(() => new PageNavigationHistory(1));
        });
        test("Default viewport is usable while invalid transforms are rejected", () => {
            new PageViewport().Validate(); new PageViewport(119000, 119000, 1).Validate(); Check(new PageViewport().Zoom == 1);
            Throws<ArgumentOutOfRangeException>(() => new PageViewport(0, -1, 1).Validate());
            Throws<ArgumentOutOfRangeException>(() => new PageViewport(0, 0, 0).Validate());
        });
        test("Desktop pane layout keeps requested widths when space is available", () => {
            var n = new NavigationPreferences(); Check(NavigationPaneLayout.Fit(1600, n, true, true, true) == new NavigationPaneWidths(204, 206, 310));
        });
        test("Pane layout compresses without mutating saved widths or losing paper space", () => {
            var n = new NavigationPreferences { NotebookWidth = 480, PageWidth = 480, SearchWidth = 520 };
            foreach (var width in new[] { 300d, 500d, 900d, 1100d })
            {
                var fitted = NavigationPaneLayout.Fit(width, n, true, true, true);
                Check(fitted.Notebooks >= 0 && fitted.Pages >= 0 && fitted.Search >= 0 && fitted.Notebooks + fitted.Pages + fitted.Search <= width - 280 + 0.001);
            }
            Check(n.NotebookWidth == 480 && n.SearchWidth == 520);
        });
        test("Hidden panes do not consume width and invalid layout requests fail", () => {
            var fitted = NavigationPaneLayout.Fit(900, new(), false, false, true); Check(fitted.Notebooks == 0 && fitted.Pages == 0 && fitted.Search == 310);
            Throws<ArgumentOutOfRangeException>(() => NavigationPaneLayout.Fit(double.NaN, new(), true, true, true));
            Check(NavigationPaneLayout.Fit(0, new(), true, true, true) == new NavigationPaneWidths());
        });
    }
}
