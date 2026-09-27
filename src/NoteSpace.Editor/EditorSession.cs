using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed record DocumentChange(string Description, bool StructureChanged);
public sealed record SearchHit(string NotebookId, string SectionId, string PageId, string PageTitle, string Snippet, string? BlockId);

/// <summary>A UI-independent, single-writer editor. Mutations must use transactions.
/// Generic transactions snapshot the workspace; page transactions retain only that page.</summary>
public sealed partial class EditorSession
{
    private sealed record Entry(string Before, string After, string Label, bool Structure, string? PageId = null);
    private readonly LinkedList<Entry> undo = new();
    private readonly Stack<Entry> redo = new();
    private long historyBytes;
    private bool executing;
    private Dictionary<string, (Notebook Notebook, NoteSection Section, NotePage Page)>? pageIndex;
    private Dictionary<string, NoteSection>? sectionIndex;
    private List<(Notebook Notebook, NoteSection Section, NotePage Page)>? orderedPages;
    public Workspace Document { get; private set; }
    public event EventHandler<DocumentChange>? Changed;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string UndoDescription => undo.Last?.Value.Label ?? "";
    /// <summary>Serialized UTF-16 characters retained by undo and redo (not bytes or process memory).</summary>
    public long RetainedHistoryCharacters => historyBytes + redo.Sum(e => (long)e.Before.Length + e.After.Length);
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public EditorSession(Workspace document)
    {
        DocumentJson.Validate(document); SerializeBounded(document); Document = document;
    }
    private IEnumerable<(Notebook Notebook, NoteSection Section, NotePage Page)> EnumeratePages()
    {
        foreach (var notebook in Document.Notebooks) foreach (var section in notebook.Sections)
            foreach (var page in section.Pages) yield return (notebook, section, page);
    }
    private void EnsureIndexes()
    {
        if (pageIndex is not null) return;
        var pages = new Dictionary<string, (Notebook, NoteSection, NotePage)>(StringComparer.Ordinal);
        var sections = new Dictionary<string, NoteSection>(StringComparer.Ordinal);
        var ordered = new List<(Notebook, NoteSection, NotePage)>();
        foreach (var notebook in Document.Notebooks) foreach (var section in notebook.Sections)
        {
            sections.Add(section.Id, section);
            foreach (var page in section.Pages) { var location = (notebook, section, page); pages.Add(page.Id, location); ordered.Add(location); }
        }
        pageIndex = pages; sectionIndex = sections; orderedPages = ordered;
    }
    /// <summary>Refresh lookup projections after a host deliberately mutates DTOs outside
    /// a transaction. Such mutations do not create history or change notifications.</summary>
    public void InvalidateIndexes() { pageIndex = null; sectionIndex = null; orderedPages = null; }
    public IEnumerable<(Notebook Notebook, NoteSection Section, NotePage Page)> Pages
    {
        get { if (executing) return EnumeratePages(); EnsureIndexes(); return orderedPages!; }
    }
    public NotePage? FindPage(string? id)
    {
        if (id is null) return null;
        if (executing) return EnumeratePages().FirstOrDefault(x => x.Page.Id == id).Page;
        EnsureIndexes(); return pageIndex!.TryGetValue(id, out var value) ? value.Page : null;
    }
    public NoteSection? FindSection(string? id)
    {
        if (id is null) return null;
        if (executing) return Document.Notebooks.SelectMany(n => n.Sections).FirstOrDefault(s => s.Id == id);
        EnsureIndexes(); return sectionIndex!.GetValueOrDefault(id);
    }
    public NotePage? SelectedPage => FindPage(Document.Settings.SelectedPageId) ?? Pages.FirstOrDefault().Page;
    public void SelectPage(string id) { if (FindPage(id) is null) throw new ArgumentException("Page does not exist.", nameof(id)); Document.Settings.SelectedPageId = id; }
    private static string SerializeBounded(Workspace document)
    {
        var json = DocumentJson.Serialize(document);
        if (json.Length > DocumentJson.MaxJsonLength) throw new InvalidDataException("The edit would exceed the 32 MiB notebook limit. The previous document has been retained.");
        return json;
    }
    public void Execute(string label, Action<Workspace> edit, bool structure = false)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (executing) throw new InvalidOperationException("Nested document transactions are not supported.");
        var before = SerializeBounded(Document); var revision = Document.Revision;
        string after;
        executing = true; InvalidateIndexes();
        try
        {
            edit(Document); DocumentJson.Validate(Document); after = SerializeBounded(Document);
            if (before == after) return;
            Document.Revision = checked(Math.Max(revision, Document.Revision) + 1);
            after = SerializeBounded(Document);
        }
        catch { Document = DocumentJson.Deserialize(before); throw; }
        finally { executing = false; InvalidateIndexes(); }
        Record(new(before, after, label, structure));
    }

    /// <summary>Edit a detached page draft, then validate the complete workspace and its
    /// aggregate size before committing. The callback must edit only the supplied draft.
    /// Successful edits replace the page DTO; retain IDs, not mutable object references.</summary>
    public void EditPage(string pageId, string label, Action<NotePage> edit, bool structure = false)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (executing) throw new InvalidOperationException("Nested document transactions are not supported.");
        EnsureIndexes();
        if (!pageIndex!.TryGetValue(pageId, out var location)) throw new ArgumentException("Page not found.", nameof(pageId));
        var position = location.Section.Pages.IndexOf(location.Page);
        var before = DocumentJson.PageJson(location.Page);
        var draft = DocumentJson.ReadPage(before);
        var revision = Document.Revision;
        string after;
        executing = true;
        try
        {
            edit(draft);
            if (draft.Id != pageId) throw new InvalidOperationException("A page edit cannot change the page identity.");
            // Do not manufacture revisions/autosaves for an unchanged value.
            after = DocumentJson.PageJson(draft); if (before == after) return;
            draft.Modified = DateTimeOffset.Now;
            after = DocumentJson.PageJson(draft);
            location.Section.Pages[position] = draft;
            Document.Revision = checked(revision + 1);
            DocumentJson.Validate(Document);
            // Full size validation is deliberately retained; no estimate can bypass
            // attachment, escaping, version-history, or aggregate workspace limits.
            SerializeBounded(Document);
        }
        catch { location.Section.Pages[position] = location.Page; Document.Revision = revision; throw; }
        finally { executing = false; InvalidateIndexes(); }
        Record(new(before, after, label, structure, pageId));
    }
    private void Record(Entry entry)
    {
        undo.AddLast(entry); historyBytes += entry.Before.Length + entry.After.Length;
        redo.Clear(); TrimHistory(); Changed?.Invoke(this, new DocumentChange(entry.Label, entry.Structure));
    }
    private void TrimHistory()
    {
        while (undo.Count > 1 && (undo.Count > 100 || historyBytes > 32 * 1024 * 1024))
        {
            var entry = undo.First!.Value; historyBytes -= entry.Before.Length + entry.After.Length; undo.RemoveFirst();
        }
    }
    private void Restore(Entry entry, string json)
    {
        var nextRevision = checked(Document.Revision + 1);
        if (entry.PageId is not null)
        {
            EnsureIndexes();
            if (!pageIndex!.TryGetValue(entry.PageId, out var location)) throw new InvalidOperationException("The history page no longer exists.");
            var page = DocumentJson.ReadPage(json); var at = location.Section.Pages.IndexOf(location.Page);
            var oldRevision = Document.Revision;
            location.Section.Pages[at] = page; Document.Revision = nextRevision;
            try { DocumentJson.Validate(Document); SerializeBounded(Document); }
            catch { location.Section.Pages[at] = location.Page; Document.Revision = oldRevision; throw; }
            Document.Revision = nextRevision;
        }
        else
        {
            var restored = DocumentJson.Deserialize(json); var selected = Document.Settings.SelectedPageId;
            restored.Revision = nextRevision; Document = restored; InvalidateIndexes();
            if (FindPage(selected) is not null) Document.Settings.SelectedPageId = selected;
        }
        InvalidateIndexes();
    }
    public void Undo()
    {
        if (executing) throw new InvalidOperationException("Cannot undo during a document transaction.");
        if (undo.Last is null) return;
        var entry = undo.Last.Value; Restore(entry, entry.Before);
        undo.RemoveLast(); historyBytes -= entry.Before.Length + entry.After.Length; redo.Push(entry);
        Changed?.Invoke(this, new DocumentChange("Undo " + entry.Label, entry.PageId is null || entry.Structure));
    }
    public void Redo()
    {
        if (executing) throw new InvalidOperationException("Cannot redo during a document transaction.");
        if (!redo.TryPeek(out var entry)) return;
        Restore(entry, entry.After);
        redo.Pop(); undo.AddLast(entry); historyBytes += entry.Before.Length + entry.After.Length; TrimHistory();
        Changed?.Invoke(this, new DocumentChange("Redo " + entry.Label, entry.PageId is null || entry.Structure));
    }
    public void Replace(Workspace document)
    {
        DocumentJson.Validate(document);
        Execute("Import notebook", _ => Document = DocumentJson.Clone(document), true);
    }
}
