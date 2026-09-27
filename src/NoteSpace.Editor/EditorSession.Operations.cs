using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed partial class EditorSession
{
    public Notebook AddNotebook(string title)
    {
        var n = new Notebook { Title = CleanTitle(title), Sections = [new NoteSection { Title = "Notes", Pages = [new NotePage()] }] };
        Execute("New notebook", w => { w.Notebooks.Add(n); w.Settings.SelectedPageId = n.Sections[0].Pages[0].Id; }, true); return n;
    }
    public NoteSection AddSection(string notebookId, string title)
    {
        var s = new NoteSection { Title = CleanTitle(title), Pages = [new NotePage()] };
        Execute("New section", w => { var n = w.Notebooks.First(n => n.Id == notebookId); n.Sections.Add(s); w.Settings.SelectedPageId = s.Pages[0].Id; }, true); return s;
    }
    public NotePage AddPage(string sectionId, string title = "Untitled page")
    {
        var p = new NotePage { Title = CleanTitle(title) };
        Execute("New page", w => { (FindSection(sectionId) ?? throw new ArgumentException("Section not found.")).Pages.Add(p); w.Settings.SelectedPageId = p.Id; }, true); return p;
    }
    public void EditPage(string pageId, string label, Action<NotePage> edit, bool structure = false) => Execute(label, _ => { var p = FindPage(pageId) ?? throw new ArgumentException("Page not found."); edit(p); p.Modified = DateTimeOffset.Now; }, structure);
    public void RenamePage(string pageId, string title) => EditPage(pageId, "Rename page", p => p.Title = CleanTitle(title), true);
    public void DeletePage(string pageId)
    {
        Execute("Delete page", w => {
            var item = Pages.First(p => p.Page.Id == pageId);
            w.Trash.Add(new DeletedPage { SectionId = item.Section.Id, Page = item.Page }); item.Section.Pages.Remove(item.Page);
            if (w.Settings.SelectedPageId == pageId) w.Settings.SelectedPageId = item.Section.Pages.FirstOrDefault()?.Id ?? Pages.FirstOrDefault().Page?.Id;
        }, true);
    }
    public void RestorePage(string pageId, string? destinationSectionId = null)
    {
        Execute("Restore page", w => {
            var item = w.Trash.First(d => d.Page.Id == pageId);
            var section = FindSection(destinationSectionId ?? item.SectionId) ?? w.Notebooks.SelectMany(n => n.Sections).FirstOrDefault() ?? throw new InvalidOperationException("Create a section before restoring.");
            section.Pages.Add(item.Page); w.Trash.Remove(item); w.Settings.SelectedPageId = item.Page.Id;
        }, true);
    }
    public NotePage DuplicatePage(string pageId)
    {
        var source = Pages.First(x => x.Page.Id == pageId);
        var p = DocumentJson.ReadPage(DocumentJson.PageJson(source.Page)); p.Id = Ids.New(); p.Title = CleanTitle(p.Title + " (copy)"); p.Versions.Clear();
        foreach (var b in p.Blocks) b.Id = Ids.New(); foreach (var s in p.Ink) s.Id = Ids.New();
        Execute("Duplicate page", w => { source.Section.Pages.Insert(source.Section.Pages.IndexOf(source.Page) + 1, p); w.Settings.SelectedPageId = p.Id; }, true); return p;
    }
    public void MovePage(string pageId, string sectionId, int index)
    {
        Execute("Move page", _ => { var source = Pages.First(x => x.Page.Id == pageId); var target = FindSection(sectionId) ?? throw new ArgumentException("Section not found."); source.Section.Pages.Remove(source.Page); target.Pages.Insert(Math.Clamp(index, 0, target.Pages.Count), source.Page); }, true);
    }
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
    public IEnumerable<SearchHit> Search(string query, bool tagsOnly = false)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        query = query.Trim();
        foreach (var (n, s, p) in Pages)
        {
            if (!tagsOnly && p.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) yield return new(n.Id, s.Id, p.Id, p.Title, p.Title, null);
            foreach (var b in p.Blocks)
            {
                var content = b.Text + " " + string.Join(" ", b.Cells.SelectMany(r => r));
                var at = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if ((!tagsOnly && at >= 0) || b.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)))
                { var start = Math.Max(0, at - 30); yield return new(n.Id, s.Id, p.Id, p.Title, content.Substring(start, Math.Min(140, content.Length - start)), b.Id); }
            }
        }
    }
    public static string CleanTitle(string title) => string.IsNullOrWhiteSpace(title) ? "Untitled" : title.Trim()[..Math.Min(title.Trim().Length, 500)];
}

public static class RichText
{
    // Preserve formatting outside the minimal replaced range, including surrogate pairs.
    public static void Replace(NoteBlock block, string value)
    {
        var old = block.Text; var prefix = 0;
        while (prefix < old.Length && prefix < value.Length && old[prefix] == value[prefix]) prefix++;
        if (prefix > 0 && prefix < old.Length && char.IsLowSurrogate(old[prefix])) prefix--;
        var suffix = 0;
        while (suffix < old.Length - prefix && suffix < value.Length - prefix && old[old.Length - 1 - suffix] == value[value.Length - 1 - suffix]) suffix++;
        if (suffix > 0 && suffix < old.Length && char.IsLowSurrogate(old[old.Length - suffix])) suffix--;
        var removedEnd = old.Length - suffix; var delta = value.Length - old.Length;
        var result = new List<TextMark>();
        foreach (var m in block.Marks)
        {
            var end = m.Start + m.Length;
            if (end <= prefix) result.Add(m);
            else if (m.Start >= removedEnd) { m.Start += delta; result.Add(m); }
            else
            {
                if (m.Start < prefix) result.Add(new TextMark { Start = m.Start, Length = prefix - m.Start, Format = DocumentJson.CloneFormat(m.Format), Link = m.Link });
                if (end > removedEnd) result.Add(new TextMark { Start = removedEnd + delta, Length = end - removedEnd, Format = DocumentJson.CloneFormat(m.Format), Link = m.Link });
            }
        }
        block.Text = value; block.Marks = result;
    }
    public static void Apply(NoteBlock block, int start, int length, Action<TextFormat> format, string? link = null)
    {
        if (start < 0 || length < 0 || (long)start + length > block.Text.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (length == 0) { format(block.Format); return; }
        var f = DocumentJson.CloneFormat(At(block, start)); format(f);
        block.Marks.Add(new TextMark { Start = start, Length = length, Format = f, Link = link });
    }
    public static TextFormat At(NoteBlock b, int offset) => b.Marks.LastOrDefault(m => offset >= m.Start && offset < m.Start + m.Length)?.Format ?? b.Format;
}
