using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed partial class EditorSession
{
    public Notebook AddNotebook(string title)
    {
        var n = new Notebook { Title = CleanTitle(title), Sections = [new NoteSection { Title = "Notes", Pages = [new NotePage()] }] };
        Execute("New notebook", w => { w.Notebooks.Add(n); w.Settings.SelectedPageId = n.Sections[0].Pages[0].Id; }, true); return n;
    }
    public NoteSection AddSection(string notebookId, string title) => AddSectionInGroup(notebookId, title);
    public NotePage AddPage(string sectionId, string title = "Untitled page")
    {
        var p = new NotePage { Title = CleanTitle(title) };
        Execute("New page", w => { (FindSection(sectionId) ?? throw new ArgumentException("Section not found.")).Pages.Add(p); w.Settings.SelectedPageId = p.Id; }, true); return p;
    }
    public void RenamePage(string pageId, string title) => EditPage(pageId, "Rename page", p => p.Title = CleanTitle(title), true);
    // Deleting a parent preserves its descendants and promotes them one level.
    public void DeletePage(string pageId)
    {
        var (section, _, entry) = OutlineFor(pageId);
        Execute("Delete page", w => {
            PageOutline.Normalize(section.Pages);
            for (var i = entry.Index + 1; i < entry.EndIndex; i++) section.Pages[i].Level--;
            w.Trash.Add(new DeletedPage { SectionId = section.Id, Page = entry.Page });
            section.Pages.RemoveAt(entry.Index);
            if (w.Settings.SelectedPageId == pageId)
                w.Settings.SelectedPageId = section.Pages.ElementAtOrDefault(Math.Min(entry.Index, section.Pages.Count - 1))?.Id ?? Pages.FirstOrDefault().Page?.Id;
        }, true);
    }
    public void RestorePage(string pageId, string? destinationSectionId = null)
    {
        Execute("Restore page", w => {
            var item = w.Trash.First(d => d.Page.Id == pageId);
            var section = FindSection(destinationSectionId ?? item.SectionId) ?? w.Notebooks.SelectMany(n => n.Sections).FirstOrDefault() ?? throw new InvalidOperationException("Create a section before restoring.");
            item.Page.Level = 0; item.Page.IsCollapsed = false;
            section.Pages.Add(item.Page); w.Trash.Remove(item); w.Settings.SelectedPageId = item.Page.Id;
        }, true);
    }
    public NotePage DuplicatePage(string pageId)
    {
        var source = Pages.First(x => x.Page.Id == pageId);
        var (_, _, entry) = OutlineFor(pageId);
        var p = DocumentJson.ReadPage(DocumentJson.PageJson(source.Page)); p.Id = Ids.New(); p.Title = CleanTitle(p.Title + " (copy)"); p.Versions.Clear(); p.Level = entry.Level; p.IsCollapsed = false; p.Created = p.Modified = DateTimeOffset.Now;
        foreach (var b in p.Blocks) b.Id = Ids.New(); foreach (var s in p.Ink) s.Id = Ids.New();
        Execute("Duplicate page", w => { PageOutline.Normalize(source.Section.Pages); source.Section.Pages.Insert(entry.EndIndex, p); w.Settings.SelectedPageId = p.Id; }, true); return p;
    }
    public void MovePage(string pageId, string sectionId, int index) => MovePageGroup(pageId, sectionId, index);
    public void AddBlock(string pageId, NoteBlock block) => EditPage(pageId, "Insert note", p => p.Blocks.Add(block));
    public void DeleteBlock(string pageId, string blockId) => EditPage(pageId, "Delete note", p => p.Blocks.RemoveAll(b => b.Id == blockId));
    public void UpdateText(string pageId, string blockId, string text) => EditPage(pageId, "Edit text", p => { var b = p.Blocks.First(b => b.Id == blockId); RichText.Replace(b, text); });
    public void SaveVersion(string pageId, string label = "Saved version") => EditPage(pageId, "Save page version", p => {
        var copy = DocumentJson.ReadPage(DocumentJson.PageJson(p)); copy.Versions.Clear();
        p.Versions.Add(new PageVersion { Description = label, Json = DocumentJson.PageJson(copy) }); if (p.Versions.Count > 20) p.Versions.RemoveAt(0);
    });
    public void RestoreVersion(string pageId, int index) => EditPage(pageId, "Restore page version", p => {
        var snapshot = DocumentJson.ReadPage(p.Versions[index].Json);
        p.Title = snapshot.Title; p.Blocks = snapshot.Blocks; p.Ink = snapshot.Ink; p.Paper = snapshot.Paper; p.PaperColor = snapshot.PaperColor;
    }, true);
    public IEnumerable<SearchHit> Search(string query, bool tagsOnly = false) =>
        string.IsNullOrWhiteSpace(query) ? [] : SearchCore(new(query.Trim()) {
            Filter = tagsOnly ? NoteSearchFilter.TagsOnly : NoteSearchFilter.AllContent,
            MaximumResults = int.MaxValue
        }, default);
    public static string CleanTitle(string title) => string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim()[..Math.Min(title.Trim().Length, 500)];
}
