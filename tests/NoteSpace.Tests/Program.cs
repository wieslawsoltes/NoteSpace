using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Storage;

var passed = 0; var failed = 0;
void Test(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); passed++; } catch (Exception e) { Console.Error.WriteLine("FAIL " + name + ": " + e); failed++; } }
void Assert(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
Test("Sample validates and JSON round-trips", () => { var w = SampleWorkspace.Create(); var json = DocumentJson.Serialize(w); Assert(DocumentJson.Serialize(DocumentJson.Deserialize(json)) == json); });
Test("Unsupported schemas rejected", () => Throws<InvalidDataException>(() => DocumentJson.Deserialize("{\"schemaVersion\":999}")));
Test("Duplicate identities rejected", () => { var w = SampleWorkspace.Create(); w.Notebooks[0].Sections[0].Pages.Add(w.Notebooks[0].Sections[0].Pages[0]); Throws<InvalidDataException>(() => DocumentJson.Validate(w)); });
Test("Null collections rejected", () => Throws<InvalidDataException>(() => DocumentJson.Deserialize("{\"schemaVersion\":1,\"notebooks\":null}")));
Test("Non-finite geometry rejected", () => { var w = SampleWorkspace.Create(); w.Notebooks[0].Sections[0].Pages[0].Blocks[0].X = float.NaN; Throws<InvalidDataException>(() => DocumentJson.Validate(w)); });
Test("Undo and redo restore title", () => { var s = new EditorSession(SampleWorkspace.Create()); var id = s.SelectedPage!.Id; var old = s.SelectedPage.Title; s.RenamePage(id, "Changed"); s.Undo(); Assert(s.FindPage(id)!.Title == old); s.Redo(); Assert(s.FindPage(id)!.Title == "Changed"); });
Test("Revision monotonically increases across undo", () => { var s = new EditorSession(SampleWorkspace.Create()); s.RenamePage(s.SelectedPage!.Id, "x"); var r = s.Document.Revision; s.Undo(); Assert(s.Document.Revision > r); });
Test("Failed transaction rolls back", () => { var s = new EditorSession(SampleWorkspace.Create()); var before = DocumentJson.Serialize(s.Document); Throws<InvalidOperationException>(() => s.Execute("bad", w => { w.Notebooks.Clear(); throw new InvalidOperationException(); })); Assert(DocumentJson.Serialize(s.Document) == before); Assert(!s.CanUndo); });
Test("Invalid transaction rolls back", () => { var s = new EditorSession(SampleWorkspace.Create()); Throws<InvalidDataException>(() => s.EditPage(s.SelectedPage!.Id, "bad", p => p.Blocks[0].Width = -2)); Assert(s.SelectedPage!.Blocks[0].Width > 0); });
Test("New edits discard redo", () => { var s = new EditorSession(SampleWorkspace.Create()); s.RenamePage(s.SelectedPage!.Id, "a"); s.Undo(); s.RenamePage(s.SelectedPage!.Id, "b"); Assert(!s.CanRedo); });
Test("New notebook has usable section and page", () => { var s = new EditorSession(SampleWorkspace.Create()); var n = s.AddNotebook("Research"); Assert(n.Sections[0].Pages[0].Id == s.SelectedPage!.Id); });
Test("Delete and restore preserve content", () => { var s = new EditorSession(SampleWorkspace.Create()); var p = s.SelectedPage!; s.DeletePage(p.Id); Assert(s.FindPage(p.Id) is null); s.RestorePage(p.Id); Assert(s.FindPage(p.Id)!.Blocks.Count == p.Blocks.Count); });
Test("Duplicate pages receive distinct identities", () => { var s = new EditorSession(SampleWorkspace.Create()); var p = s.SelectedPage!; var copy = s.DuplicatePage(p.Id); Assert(copy.Id != p.Id); Assert(copy.Blocks[0].Id != p.Blocks[0].Id); DocumentJson.Validate(s.Document); });
Test("Move page between sections", () => { var s = new EditorSession(SampleWorkspace.Create()); var p = s.SelectedPage!; var target = s.Document.Notebooks[0].Sections[1]; s.MovePage(p.Id, target.Id, 0); Assert(target.Pages[0].Id == p.Id); });
Test("Text and table search", () => { var s = new EditorSession(SampleWorkspace.Create()); Assert(s.Search("design review").Any()); Assert(s.Search("FRESH START").Any()); });
Test("Tag-only search", () => { var s = new EditorSession(SampleWorkspace.Create()); s.SelectedPage!.Blocks[0].Tags.Add("Important"); Assert(s.Search("important", true).Count() == 1); });
Test("Rich text ranges apply", () => { var b = new NoteBlock { Text = "Hello world" }; RichText.Apply(b, 6, 5, f => f.Bold = true); Assert(!RichText.At(b, 0).Bold && RichText.At(b, 8).Bold); });
Test("Text insertion shifts later formatting", () => { var b = new NoteBlock { Text = "Hello world" }; RichText.Apply(b, 6, 5, f => f.Bold = true); RichText.Replace(b, "Hi Hello world"); Assert(b.Marks[0].Start == 9); });
Test("Text deletion clips formatting", () => { var b = new NoteBlock { Text = "Hello world" }; RichText.Apply(b, 0, 11, f => f.Bold = true); RichText.Replace(b, "Heorld"); Assert(b.Marks.Sum(m => m.Length) == 6); });
Test("Invalid text ranges rejected", () => Throws<ArgumentOutOfRangeException>(() => RichText.Apply(new NoteBlock { Text = "abc" }, 1, int.MaxValue, f => f.Bold = true)));
Test("Version restore preserves history", () => { var s = new EditorSession(SampleWorkspace.Create()); var id = s.SelectedPage!.Id; var title = s.SelectedPage.Title; s.SaveVersion(id); s.RenamePage(id, "edited"); s.RestoreVersion(id, 0); Assert(s.FindPage(id)!.Title == title && s.FindPage(id)!.Versions.Count == 1); });
Test("File store detects stale writes", () => { var folder = Path.Combine(Path.GetTempPath(), Ids.New()); Directory.CreateDirectory(folder); try { var store = new FileWorkspaceStore(Path.Combine(folder, "notebook.json")); var w = SampleWorkspace.Create(); var token = store.SaveAsync(w, null).GetAwaiter().GetResult(); Assert(store.LoadAsync().GetAwaiter().GetResult()!.Token == token); Throws<StorageConflictException>(() => store.SaveAsync(w, "stale").GetAwaiter().GetResult()); } finally { Directory.Delete(folder, true); } });
Test("Oversized aggregate edits roll back atomically", () => {
    var s = new EditorSession(SampleWorkspace.Create()); var before = DocumentJson.Serialize(s.Document);
    Throws<InvalidDataException>(() => s.EditPage(s.SelectedPage!.Id, "Oversize", p => p.Blocks = Enumerable.Range(0, 17).Select(_ => new NoteBlock { Text = new string('x', 2 * 1024 * 1024) }).ToList()));
    Assert(DocumentJson.Serialize(s.Document) == before); Assert(!s.CanUndo);
});
Test("Nested transactions are rejected and rolled back", () => {
    var s = new EditorSession(SampleWorkspace.Create()); var before = DocumentJson.Serialize(s.Document);
    Throws<InvalidOperationException>(() => s.Execute("Outer", w => { w.Notebooks[0].Title = "Changed"; s.Execute("Nested", _ => { }); }));
    Assert(DocumentJson.Serialize(s.Document) == before); Assert(!s.CanUndo);
});
Test("Import preserves monotonically increasing revision", () => {
    var s = new EditorSession(SampleWorkspace.Create()); s.Document.Revision = 42;
    s.Replace(SampleWorkspace.Create()); Assert(s.Document.Revision == 43); s.Undo(); Assert(s.Document.Revision == 44);
});
Test("A rejected edit does not discard redo history", () => {
    var s = new EditorSession(SampleWorkspace.Create()); var id = s.SelectedPage!.Id;
    s.RenamePage(id, "Retained redo"); s.Undo();
    Throws<InvalidDataException>(() => s.EditPage(id, "Invalid", p => p.Blocks[0].Width = -1));
    Assert(s.CanRedo); s.Redo(); Assert(s.SelectedPage!.Title == "Retained redo");
});
WysiwygTests.Run(Test);
NativeDraftTests.Run(Test);
EditingRegressionTests.Run(Test);
PerformanceAndTableTests.Run(Test);
OrganizationTests.Run(Test);
SearchTests.Run(Test);
SearchScopeTests.Run(Test);
SnapshotTests.Run(Test);
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
