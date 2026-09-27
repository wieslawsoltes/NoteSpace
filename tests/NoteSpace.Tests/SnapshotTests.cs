using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Storage;

internal static class SnapshotTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static Workspace Fixture(string text = "Draft") => new() { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = [new NotePage { Blocks = [new NoteBlock { Text = text }] }] }] }] };
    private static NotePage Page(Workspace w) => w.Notebooks[0].Sections[0].Pages[0];
    private static void InFolder(Action<string> action)
    {
        var folder = Path.Combine(Path.GetTempPath(), "notespace-" + Ids.New()); Directory.CreateDirectory(folder);
        try { action(Path.Combine(folder, "workspace.notespace")); } finally { Directory.Delete(folder, true); }
    }
    public static void Run(Action<string, Action> test)
    {
        test("Streaming size measurement equals the generated JSON string length", () => {
            foreach (var text in new[] { "", "ASCII", "quote\" backslash\\ newline\n", "żółć العربية 中文 😀", "\ud800", new string('x', 100000) })
            {
                var w = Fixture(text); var expected = DocumentJson.Serialize(w).Length;
                var measured = WorkspaceSerialization.MeasureCharacters(w);
                if (measured != expected) throw new Exception($"Text length {text.Length}: expected {expected} JSON characters, measured {measured}.");
            }
        });
        test("Streaming size includes attachments histories groups and escaped characters", () => {
            var w = Fixture("<>&\r\n\t"); Page(w).Blocks.Add(new NoteBlock { Kind = BlockKind.Attachment, Data = Enumerable.Range(0, 60000).Select(i => (byte)i).ToArray(), FileName = "résumé.bin" });
            Page(w).Versions.Add(new PageVersion { Json = DocumentJson.PageJson(Page(Fixture("old"))) });
            Check(WorkspaceSerialization.MeasureCharacters(w) == DocumentJson.Serialize(w).Length);
        });
        test("Streaming size enforces the exact inclusive character boundary", () => {
            var w = Fixture(); var size = DocumentJson.Serialize(w).Length;
            Check(WorkspaceSerialization.MeasureCharacters(w, size) == size);
            Throws<InvalidDataException>(() => WorkspaceSerialization.MeasureCharacters(w, size - 1));
            Throws<ArgumentOutOfRangeException>(() => WorkspaceSerialization.MeasureCharacters(w, -1));
            Throws<ArgumentNullException>(() => WorkspaceSerialization.MeasureCharacters(null!));
        });
        test("Snapshot capture detaches text and attachment data from future edits", () => {
            var w = Fixture(); Page(w).Blocks[0].Data = [1, 2, 3]; w.Revision = 7;
            var snapshot = WorkspaceSnapshot.Capture(w); var json = snapshot.Json;
            Page(w).Blocks[0].Text = "Changed"; Page(w).Blocks[0].Data![0] = 9; w.Revision = 8;
            var saved = DocumentJson.Deserialize(snapshot.Json);
            Check(snapshot.Json == json && snapshot.Revision == 7 && saved.Revision == 7 && Page(saved).Blocks[0].Text == "Draft" && Page(saved).Blocks[0].Data![0] == 1);
        });
        test("Snapshot capture rejects invalid documents and oversized workspaces", () => {
            var w = Fixture(); Page(w).Blocks[0].Width = -1;
            Throws<InvalidDataException>(() => WorkspaceSnapshot.Capture(w));
            w = Fixture(); Page(w).Blocks = Enumerable.Range(0, 17).Select(_ => new NoteBlock { Text = new string('x', 2 * 1024 * 1024) }).ToList();
            Throws<InvalidDataException>(() => WorkspaceSnapshot.Capture(w));
        });
        test("Snapshot parse validates untrusted content without changing its representation", () => {
            var w = Fixture(); var json = " \n" + DocumentJson.Serialize(w) + "\n";
            var snapshot = WorkspaceSnapshot.Parse(json); Check(snapshot.Json == json && snapshot.Revision == w.Revision);
            Throws<InvalidDataException>(() => WorkspaceSnapshot.Parse("{\"schemaVersion\":99}"));
            Throws<InvalidDataException>(() => WorkspaceSnapshot.Parse("not-json"));
        });
        test("File snapshot saves retain token conflicts and exact validated content", () => InFolder(path => {
            var store = new FileWorkspaceStore(path); var w = Fixture(); var snapshot = WorkspaceSnapshot.Capture(w);
            var token = store.SaveSnapshotAsync(snapshot, null).GetAwaiter().GetResult();
            Page(w).Blocks[0].Text = "Later";
            Check(File.ReadAllText(path) == snapshot.Json);
            Check(Page(store.LoadAsync().GetAwaiter().GetResult()!.Document).Blocks[0].Text == "Draft");
            Throws<StorageConflictException>(() => store.SaveSnapshotAsync(WorkspaceSnapshot.Capture(w), "stale").GetAwaiter().GetResult());
            Check(File.ReadAllText(path) == snapshot.Json);
            var next = store.SaveAsync(w, token).GetAwaiter().GetResult(); Check(next != token);
        }));
        test("Cancelled snapshot save does not create or overwrite a file", () => InFolder(path => {
            var store = new FileWorkspaceStore(path); var snapshot = WorkspaceSnapshot.Capture(Fixture());
            using var cts = new CancellationTokenSource(); cts.Cancel();
            Throws<OperationCanceledException>(() => store.SaveSnapshotAsync(snapshot, null, cts.Token).GetAwaiter().GetResult()); Check(!File.Exists(path));
            var token = store.SaveSnapshotAsync(snapshot, null).GetAwaiter().GetResult();
            Throws<OperationCanceledException>(() => store.SaveSnapshotAsync(WorkspaceSnapshot.Capture(Fixture("Changed")), token, cts.Token).GetAwaiter().GetResult());
            Check(File.ReadAllText(path) == snapshot.Json && Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length == 0);
        }));
        test("Snapshots remain stable through page and workspace history", () => {
            var session = new EditorSession(Fixture()); var id = session.SelectedPage!.Id;
            var snapshot = WorkspaceSnapshot.Capture(session.Document);
            session.RenamePage(id, "New title"); session.AddNotebook("Other"); session.Undo(); session.Undo();
            Check(DocumentJson.Deserialize(snapshot.Json).Revision == 0 && Page(DocumentJson.Deserialize(snapshot.Json)).Title == "Untitled page");
        });
    }
}
