using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed record DocumentChange(string Description, bool StructureChanged);
public sealed record SearchHit(string NotebookId, string SectionId, string PageId, string PageTitle, string Snippet, string? BlockId);

/// <summary>A UI-independent, single-writer editor. All writes are undoable transactions.</summary>
public sealed partial class EditorSession
{
    private sealed record Entry(string Before, string After, string Label, bool Structure);
    private readonly LinkedList<Entry> undo = new();
    private readonly Stack<Entry> redo = new();
    private long historyBytes;
    private bool executing;
    public Workspace Document { get; private set; }
    public event EventHandler<DocumentChange>? Changed;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string UndoDescription => undo.Last?.Value.Label ?? "";
    public EditorSession(Workspace document)
    {
        DocumentJson.Validate(document); SerializeBounded(document); Document = document;
    }
    public IEnumerable<(Notebook Notebook, NoteSection Section, NotePage Page)> Pages => Document.Notebooks.SelectMany(n => n.Sections.SelectMany(s => s.Pages.Select(p => (n, s, p))));
    public NotePage? FindPage(string? id) => Pages.FirstOrDefault(x => x.Page.Id == id).Page;
    public NoteSection? FindSection(string? id) => Document.Notebooks.SelectMany(n => n.Sections).FirstOrDefault(s => s.Id == id);
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
        executing = true;
        try
        {
            edit(Document); DocumentJson.Validate(Document); after = SerializeBounded(Document);
            if (before == after) return;
            Document.Revision = checked(Math.Max(revision, Document.Revision) + 1);
            after = SerializeBounded(Document);
        }
        catch { Document = DocumentJson.Deserialize(before); throw; }
        finally { executing = false; }
        undo.AddLast(new Entry(before, after, label, structure)); historyBytes += before.Length + after.Length;
        redo.Clear(); TrimHistory();
        Changed?.Invoke(this, new DocumentChange(label, structure));
    }
    private void TrimHistory()
    {
        while (undo.Count > 1 && (undo.Count > 100 || historyBytes > 32 * 1024 * 1024))
        {
            var entry = undo.First!.Value; historyBytes -= entry.Before.Length + entry.After.Length; undo.RemoveFirst();
        }
    }
    public void Undo()
    {
        if (executing) throw new InvalidOperationException("Cannot undo during a document transaction.");
        if (undo.Last is null) return;
        var entry = undo.Last.Value;
        var restored = DocumentJson.Deserialize(entry.Before);
        restored.Revision = checked(Document.Revision + 1);
        var selected = Document.Settings.SelectedPageId;
        Document = restored;
        if (FindPage(selected) is not null) Document.Settings.SelectedPageId = selected;
        undo.RemoveLast(); historyBytes -= entry.Before.Length + entry.After.Length;
        redo.Push(entry); Changed?.Invoke(this, new DocumentChange("Undo " + entry.Label, true));
    }
    public void Redo()
    {
        if (executing) throw new InvalidOperationException("Cannot redo during a document transaction.");
        if (!redo.TryPeek(out var entry)) return;
        var restored = DocumentJson.Deserialize(entry.After);
        restored.Revision = checked(Document.Revision + 1);
        var selected = Document.Settings.SelectedPageId;
        Document = restored;
        if (FindPage(selected) is not null) Document.Settings.SelectedPageId = selected;
        redo.Pop(); undo.AddLast(entry); historyBytes += entry.Before.Length + entry.After.Length; TrimHistory();
        Changed?.Invoke(this, new DocumentChange("Redo " + entry.Label, true));
    }
    public void Replace(Workspace document)
    {
        DocumentJson.Validate(document);
        Execute("Import notebook", _ => Document = DocumentJson.Clone(document), true);
    }
}
