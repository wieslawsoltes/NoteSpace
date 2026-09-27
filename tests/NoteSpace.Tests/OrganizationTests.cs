using NoteSpace.Core;
using NoteSpace.Editor;

internal static class OrganizationTests
{
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static EditorSession Session() => new(SampleWorkspace.Create());
    private static string Snapshot(EditorSession s) => DocumentJson.Serialize(s.Document);
    private static string Topology(EditorSession s) => string.Join(";", s.Document.Notebooks.Select(n => n.Id + ":" + string.Join(",", NotebookGroups.Build(n, includeCollapsed: true).Select(x => x.Id + ":" + x.Level))));
    private static (EditorSession Session, string SectionId, string[] Ids) Pages(params int[] levels)
    {
        var section = new NoteSection { Pages = levels.Select((level, i) => new NotePage { Title = "P" + i, Level = level }).ToList() };
        var workspace = new Workspace { Notebooks = [new Notebook { Sections = [section] }] };
        workspace.Settings.SelectedPageId = section.Pages.FirstOrDefault()?.Id;
        return (new(workspace), section.Id, section.Pages.Select(p => p.Id).ToArray());
    }
    private static string Order(EditorSession s, string id) => string.Join(",", s.FindSection(id)!.Pages.Select(p => p.Title + ":" + p.Level));
    public static void Run(Action<string, Action> test)
    {
        test("Schema-one files without groups remain readable", () => {
            var s = Session(); var json = Snapshot(s).Replace("\"sectionGroups\":[],", "").Replace(",\"sectionGroups\":[]", "");
            var loaded = DocumentJson.Deserialize(json); Check(loaded.Notebooks[0].SectionGroups.Count == 0);
            Check(loaded.Notebooks.Sum(n => n.Sections.Sum(x => x.Pages.Count)) == s.Pages.Count());
        });
        test("Group create and rename are undoable", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "Research");
            s.RenameSectionGroup(g.Id, "Work"); s.Undo(); Check(s.FindSectionGroup(g.Id)!.Title == "Research");
            s.Undo(); Check(s.FindSectionGroup(g.Id) is null); s.Redo(); Check(s.FindSectionGroup(g.Id)!.Title == "Research");
        });
        test("Group backup includes nesting and collapsed state", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "Research"); var child = s.AddSectionGroup(n.Id, "Ideas", g.Id);
            var section = s.AddSectionInGroup(n.Id, "Notes", child.Id); s.SetSectionGroupCollapsed(g.Id, true);
            var copy = new EditorSession(DocumentJson.Deserialize(Snapshot(s)));
            Check(copy.FindSectionGroup(child.Id)!.ParentId == g.Id && copy.FindSectionGroup(g.Id)!.IsCollapsed && copy.FindSection(section.Id)!.GroupId == child.Id);
        });
        test("Group paths preserve duplicate titles by ID", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var a = s.AddSectionGroup(n.Id, "Same"); var b = s.AddSectionGroup(n.Id, "Same", a.Id);
            Check(NotebookGroups.Ancestors(n, b.Id).SequenceEqual([a.Id, b.Id])); Check(NotebookGroups.Path(n, b.Id).EndsWith("Same / Same"));
        });
        test("New grouped sections select their starter page and expand ancestors", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "G"); s.SetSectionGroupCollapsed(g.Id, true);
            var section = s.AddSectionInGroup(n.Id, "S", g.Id); Check(!g.IsCollapsed && s.SelectedPage!.Id == section.Pages[0].Id && section.GroupId == g.Id);
        });
        test("Collapsed group hides its subtree without mutating it", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "G"); var child = s.AddSectionGroup(n.Id, "Child", g.Id);
            var section = s.AddSectionInGroup(n.Id, "S", child.Id); s.SetSectionGroupCollapsed(g.Id, true);
            var before = Snapshot(s); Check(!NotebookGroups.Build(n).Any(x => x.Id == section.Id));
            Check(NotebookGroups.Build(n, section.Id).Any(x => x.Id == section.Id)); Check(Snapshot(s) == before);
        });
        test("Group expansion view contains every section exactly once", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "G"); s.MoveSection(n.Sections[0].Id, n.Id, g.Id); s.SetSectionGroupCollapsed(g.Id, true);
            var all = NotebookGroups.Build(n, includeCollapsed: true).Where(x => x.Section is not null).Select(x => x.Id).Order();
            Check(all.SequenceEqual(n.Sections.Select(x => x.Id).Order()));
        });
        test("Section moves preserve content and selected page", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var section = n.Sections[0]; var selected = s.SelectedPage!.Id;
            var page = DocumentJson.PageJson(section.Pages[0]); var g = s.AddSectionGroup(n.Id, "G"); s.MoveSection(section.Id, n.Id, g.Id);
            Check(section.GroupId == g.Id && s.SelectedPage!.Id == selected && DocumentJson.PageJson(section.Pages[0]) == page);
            s.Undo(); Check(s.FindSection(section.Id)!.GroupId is null); s.Redo(); Check(s.FindSection(section.Id)!.GroupId == g.Id);
        });
        test("Sections move between notebooks and back to root", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var id = n.Sections[0].Id; var target = s.AddNotebook("Target"); var g = s.AddSectionGroup(target.Id, "G");
            s.MoveSection(id, target.Id, g.Id); Check(!n.Sections.Any(x => x.Id == id) && target.Sections.Any(x => x.Id == id));
            s.MoveSection(id, n.Id); Check(s.FindSection(id)!.GroupId is null && n.Sections.Any(x => x.Id == id));
        });
        test("Moving a group transfers all descendant groups and their sections", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var a = s.AddSectionGroup(n.Id, "A"); var b = s.AddSectionGroup(n.Id, "B", a.Id);
            var section = s.AddSectionInGroup(n.Id, "Inside", b.Id); var target = s.AddNotebook("Target"); var parent = s.AddSectionGroup(target.Id, "Parent");
            var before = Topology(s); s.MoveSectionGroup(a.Id, target.Id, parent.Id);
            Check(!n.SectionGroups.Any() && !n.Sections.Contains(section) && target.Sections.Contains(section) && b.ParentId == a.Id && a.ParentId == parent.Id);
            s.Undo(); Check(Topology(s) == before); s.Redo(); DocumentJson.Validate(s.Document);
        });
        test("Moving a group beneath itself or a descendant changes nothing", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var a = s.AddSectionGroup(n.Id, "A"); var b = s.AddSectionGroup(n.Id, "B", a.Id); var before = Snapshot(s);
            Throws<InvalidOperationException>(() => s.MoveSectionGroup(a.Id, n.Id, a.Id)); Throws<InvalidOperationException>(() => s.MoveSectionGroup(a.Id, n.Id, b.Id)); Check(Snapshot(s) == before);
        });
        test("Section and group destinations are notebook-local", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var a = s.AddSectionGroup(n.Id, "A"); var target = s.AddNotebook("Target"); var before = Snapshot(s);
            Throws<ArgumentException>(() => s.MoveSection(n.Sections[0].Id, target.Id, a.Id));
            Throws<ArgumentException>(() => s.AddSectionInGroup(target.Id, "Invalid", a.Id)); Check(Snapshot(s) == before);
        });
        test("Ungroup keeps direct sections and reparents child groups", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var parent = s.AddSectionGroup(n.Id, "Parent"); var a = s.AddSectionGroup(n.Id, "A", parent.Id); var b = s.AddSectionGroup(n.Id, "B", a.Id);
            var section = s.AddSectionInGroup(n.Id, "S", a.Id); var count = s.Pages.Count(); s.UngroupSectionGroup(a.Id);
            Check(s.FindSectionGroup(a.Id) is null && b.ParentId == parent.Id && section.GroupId == parent.Id && s.Pages.Count() == count && s.Document.Trash.Count == 0);
            s.Undo(); Check(s.FindSectionGroup(b.Id)!.ParentId == a.Id && s.FindSection(section.Id)!.GroupId == a.Id);
        });
        test("Ungroup at notebook root keeps nested notes reachable", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var a = s.AddSectionGroup(n.Id, "A"); var section = s.AddSectionInGroup(n.Id, "S", a.Id);
            s.UngroupSectionGroup(a.Id); Check(section.GroupId is null && s.FindPage(section.Pages[0].Id) is not null);
        });
        test("Section sibling reorder does not cross group boundaries", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "G");
            var a = s.AddSectionInGroup(n.Id, "A", g.Id); var root = s.AddSection(n.Id, "Root"); var b = s.AddSectionInGroup(n.Id, "B", g.Id);
            Check(!s.MoveSectionSibling(a.Id, -1)); Check(s.MoveSectionSibling(a.Id, 1));
            Check(n.Sections.Where(x => x.GroupId == g.Id).Select(x => x.Title).SequenceEqual(["B", "A"]) && root.GroupId is null);
        });
        test("Group sibling reorder keeps group descendants attached", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var a = s.AddSectionGroup(n.Id, "A"); var child = s.AddSectionGroup(n.Id, "Child", a.Id); var b = s.AddSectionGroup(n.Id, "B");
            Check(s.MoveSectionGroupSibling(a.Id, 1)); Check(NotebookGroups.Build(n).Where(x => x.Group is not null).Select(x => x.Id).SequenceEqual([b.Id, a.Id, child.Id]));
            Check(!s.MoveSectionGroupSibling(child.Id, 1));
        });
        test("No-op moves and collapse do not add revisions", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "G"); var r = s.Document.Revision;
            s.MoveSectionGroup(g.Id, n.Id); s.MoveSection(n.Sections[0].Id, n.Id); s.SetSectionGroupCollapsed(g.Id, false); Check(s.Document.Revision == r);
        });
        test("Invalid group graph import rejects cycles and foreign references", () => {
            var w = SampleWorkspace.Create(); var n = w.Notebooks[0]; var a = new SectionGroup(); n.SectionGroups.Add(a); a.ParentId = a.Id;
            Throws<InvalidDataException>(() => DocumentJson.Validate(w)); a.ParentId = "missing"; Throws<InvalidDataException>(() => DocumentJson.Validate(w));
            a.ParentId = null; n.Sections[0].GroupId = "missing"; Throws<InvalidDataException>(() => DocumentJson.Validate(w));
        });
        test("Null collections and identity collisions are rejected", () => {
            var w = SampleWorkspace.Create(); var n = w.Notebooks[0]; n.SectionGroups = null!; Throws<InvalidDataException>(() => DocumentJson.Validate(w));
            n.SectionGroups = [new SectionGroup { Id = n.Sections[0].Id }]; Throws<InvalidDataException>(() => DocumentJson.Validate(w));
        });
        test("Over-depth group creation rolls back and retains redo", () => {
            var s = Session(); var nId = s.Document.Notebooks[0].Id; string? parent = null;
            for (var i = 0; i < NotebookGroups.MaximumDepth; i++) parent = s.AddSectionGroup(nId, "G" + i, parent).Id;
            s.RenameSectionGroup(parent!, "Temp"); s.Undo(); var before = Snapshot(s);
            Throws<InvalidDataException>(() => s.AddSectionGroup(nId, "Too deep", parent)); Check(Snapshot(s) == before && s.CanRedo);
        });
        test("Over-depth group moves roll back without losing sections", () => {
            var s = Session(); var nId = s.Document.Notebooks[0].Id; string? parent = null;
            for (var i = 0; i < NotebookGroups.MaximumDepth; i++) parent = s.AddSectionGroup(nId, "G" + i, parent).Id;
            var g = s.AddSectionGroup(nId, "Moving"); s.AddSectionInGroup(nId, "Kept", g.Id); var before = Snapshot(s);
            Throws<InvalidDataException>(() => s.MoveSectionGroup(g.Id, nId, parent)); Check(Snapshot(s) == before);
        });
        test("Search and page recovery still see grouped sections", () => {
            var s = Session(); var n = s.Document.Notebooks[0]; var g = s.AddSectionGroup(n.Id, "G"); var section = n.Sections[0]; s.MoveSection(section.Id, n.Id, g.Id);
            Check(s.Search("Welcome").Any()); var id = section.Pages[0].Id; s.DeletePage(id); s.RestorePage(id); Check(s.FindSection(section.Id)!.Pages.Any(p => p.Id == id));
        });
        test("Random group moves preserve sections and undo topology", () => {
            var s = Session(); var nId = s.Document.Notebooks[0].Id;
            var ids = Enumerable.Range(0, 12).Select(i => s.AddSectionGroup(nId, "G" + i).Id).ToArray();
            foreach (var id in ids) s.AddSectionInGroup(nId, "S" + id, id);
            var expectedPages = s.Pages.Select(p => p.Page.Id).Order().ToArray(); var random = new Random(1246);
            for (var i = 0; i < 160; i++)
            {
                var before = Topology(s); var revision = s.Document.Revision;
                try { s.MoveSectionGroup(ids[random.Next(ids.Length)], nId, random.Next(4) == 0 ? null : ids[random.Next(ids.Length)]); }
                catch (Exception e) when (e is InvalidOperationException or InvalidDataException) { Check(Topology(s) == before); }
                DocumentJson.Validate(s.Document); Check(s.Pages.Select(p => p.Page.Id).Order().SequenceEqual(expectedPages));
                if (s.Document.Revision != revision) { var after = Topology(s); s.Undo(); Check(Topology(s) == before); s.Redo(); Check(Topology(s) == after); }
            }
        });
        test("Drop before a root moves the complete subtree", () => {
            var (s, section, ids) = Pages(0, 1, 0, 1); Check(s.MovePageRelative(new(ids[2], ids[0], PageDropPlacement.Before)));
            Check(Order(s, section) == "P2:0,P3:1,P0:0,P1:1"); s.Undo(); Check(Order(s, section) == "P0:0,P1:1,P2:0,P3:1");
        });
        test("Drop after a parent inserts after all of its descendants", () => {
            var (s, section, ids) = Pages(0, 0, 1, 2); Check(s.MovePageRelative(new(ids[0], ids[1], PageDropPlacement.After)));
            Check(Order(s, section) == "P1:0,P2:1,P3:2,P0:0");
        });
        test("Drop inside relevels a subtree and expands the target", () => {
            var (s, section, ids) = Pages(0, 1, 0, 1); s.SetPageCollapsed(ids[2], true);
            Check(s.MovePageRelative(new(ids[0], ids[2], PageDropPlacement.Inside))); Check(Order(s, section) == "P2:0,P3:1,P0:1,P1:2"); Check(!s.FindPage(ids[2])!.IsCollapsed && s.SelectedPage!.Id == ids[0]);
        });
        test("Dropping inside self or descendants is rejected without history", () => {
            var (s, _, ids) = Pages(0, 1, 2, 0); var before = Snapshot(s);
            foreach (var p in Enum.GetValues<PageDropPlacement>()) { Check(!s.MovePageRelative(new(ids[0], ids[1], p))); Check(!s.CanMovePageRelative(new(ids[0], ids[0], p))); }
            Check(Snapshot(s) == before && !s.CanUndo);
        });
        test("Drop rejects subtree depth overflow", () => {
            var (s, _, ids) = Pages(0, 1, 2, 0, 1); Check(!s.CanMovePageRelative(new(ids[3], ids[1], PageDropPlacement.Inside))); Check(!s.MovePageRelative(new(ids[3], ids[2], PageDropPlacement.Inside))); Check(!s.CanUndo);
        });
        test("Drop can move a child after its ancestor without adopting siblings", () => {
            var (s, section, ids) = Pages(0, 1, 2, 1, 0); Check(s.MovePageRelative(new(ids[1], ids[0], PageDropPlacement.After)));
            Check(Order(s, section) == "P0:0,P3:1,P1:0,P2:1,P4:0");
        });
        test("Already adjacent drops do not consume undo or change selection", () => {
            var (s, _, ids) = Pages(0, 1, 0); var before = Snapshot(s);
            Check(!s.MovePageRelative(new(ids[0], ids[2], PageDropPlacement.Before))); Check(Snapshot(s) == before);
        });
        test("Invalid and stale drop IDs are harmless", () => {
            var (s, _, ids) = Pages(0, 0); Check(!s.MovePageRelative(new("missing", ids[0], PageDropPlacement.Inside)));
            Check(!s.MovePageRelative(new(ids[0], ids[1], (PageDropPlacement)99))); Check(!s.CanUndo);
        });
        test("Cross-section drops retain descendant IDs and content", () => {
            var (s, section, ids) = Pages(0, 1); var target = s.AddSection(s.Document.Notebooks[0].Id, "Target"); var id = target.Pages[0].Id;
            Check(s.MovePageRelative(new(ids[0], id, PageDropPlacement.Inside))); Check(s.FindSection(section)!.Pages.Count == 0);
            Check(target.Pages.Select(p => p.Level).SequenceEqual([0, 1, 2]) && target.Pages[2].Id == ids[1]);
        });
        test("Drop previews never mutate orphan legacy indentation", () => {
            var (s, _, ids) = Pages(2, 2, 0); var before = Snapshot(s); s.CanMovePageRelative(new(ids[2], ids[0], PageDropPlacement.Before)); Check(Snapshot(s) == before);
        });
        test("Random page drops preserve identity, valid depth and reversible order", () => {
            var (s, section, ids) = Pages(0, 1, 2, 1, 0, 1, 0, 0); var random = new Random(671);
            for (var i = 0; i < 250; i++)
            {
                var before = Order(s, section); var changed = s.MovePageRelative(new(ids[random.Next(ids.Length)], ids[random.Next(ids.Length)], (PageDropPlacement)random.Next(3)));
                var pages = s.FindSection(section)!.Pages; Check(pages.Select(p => p.Id).Order().SequenceEqual(ids.Order()));
                Check(pages[0].Level == 0 && pages.All(p => p.Level <= 2));
                for (var j = 1; j < pages.Count; j++) Check(pages[j].Level <= pages[j - 1].Level + 1);
                if (changed) { var after = Order(s, section); s.Undo(); Check(Order(s, section) == before); s.Redo(); Check(Order(s, section) == after); }
            }
        });
    }
}
