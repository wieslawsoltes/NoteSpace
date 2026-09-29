using NoteSpace.Core;
using NoteSpace.Editor;

internal static class WysiwygTests
{
    private static void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static TextEditingBuffer Buffer(string text = "alpha beta") => new(new NoteBlock { Text = text });
    public static void Run(Action<string, Action> test)
    {
        test("Editing buffer is detached from the document", () => {
            var b = new NoteBlock { Text = "original" }; var edit = new TextEditingBuffer(b); edit.ReplaceSelection(" appended");
            Check(b.Text == "original" && edit.Block.Text == "original appended");
        });
        test("Collapsed formatting affects future typing rather than all existing text", () => {
            var b = Buffer(); b.Format(f => f.Bold = true); Check(b.Version == 0 && b.CurrentFormat.Bold && !RichText.At(b.Block, 1).Bold);
            b.ReplaceSelection("X"); Check(RichText.At(b.Block, 10).Bold && !RichText.At(b.Block, 9).Bold);
        });
        test("Selected formatting retains unrelated mixed attributes", () => {
            var b = Buffer(); b.Select(0, 5); b.Format(f => f.Bold = true); b.Select(6, 10); b.Format(f => f.Italic = true);
            b.Select(0, 10); b.Format(f => f.Underline = true);
            Check(RichText.At(b.Block, 1).Bold && !RichText.At(b.Block, 1).Italic && RichText.At(b.Block, 7).Italic && b.AllHave(f => f.Underline));
        });
        test("Typing-style toggle does not accumulate undoable content changes", () => {
            var b = Buffer(); for (var i = 0; i < 100; i++) b.Format(f => f.Bold = !f.Bold);
            Check(b.Version == 0 && b.Block.Marks.Count == 0 && !b.CurrentFormat.Bold);
        });
        test("Directional selection retains its anchor and normalizes its range", () => {
            var b = Buffer(); b.Select(8, 2); Check(b.Anchor == 8 && b.Caret == 2 && b.SelectionStart == 2 && b.SelectionLength == 6);
        });
        test("Grapheme navigation preserves emoji combining sequences and CRLF", () => {
            var b = Buffer("a😀e\u0301\r\nz"); Check(b.Adjacent(1, 1) == 3 && b.Adjacent(3, 1) == 5 && b.Adjacent(5, 1) == 7);
            Check(b.Adjacent(7, -1) == 5 && b.Snap(2) == 1 && b.Snap(4) == 3 && b.Snap(6) == 5);
        });
        test("Backspace deletes a complete grapheme and not a neighboring mark", () => {
            var b = Buffer("a😀e\u0301"); b.Delete(true); Check(b.Block.Text == "a😀"); b.Delete(true); Check(b.Block.Text == "a" && b.Caret == 1);
        });
        test("Forward deletion and directional range deletion use the same draft", () => {
            var b = Buffer("a😀bc"); b.Select(1, 1); b.Delete(false); Check(b.Block.Text == "abc"); b.Select(3, 1); b.Delete(false); Check(b.Block.Text == "a");
        });
        test("Word movement and deletion respect whitespace and punctuation", () => {
            var b = Buffer("one  two, three"); Check(b.Adjacent(0, 1, true) == 5 && b.Adjacent(15, -1, true) == 10);
            b.Delete(true, true); Check(b.Block.Text == "one  two, ");
        });
        test("Double-click word selection excludes trailing whitespace", () => {
            var b = Buffer("first second third"); b.SelectWord(8); Check(b.SelectionStart == 6 && b.SelectionLength == 6);
        });
        test("Native text reconciliation preserves surrounding runs", () => {
            var b = Buffer(); b.Select(6, 10); b.Format(f => f.Italic = true); b.Select(0, 0); b.Format(f => f.Bold = true);
            b.AcceptText("Xalpha beta"); Check(RichText.At(b.Block, 0).Bold && RichText.At(b.Block, 8).Italic && b.Caret == 1);
        });
        test("Native reconciliation handles emoji substitutions safely", () => {
            var b = Buffer("a😀b"); b.Select(3, 4); b.Format(f => f.Bold = true); b.Select(1, 3); b.AcceptText("a😃b");
            Check(b.Block.Text == "a😃b" && RichText.At(b.Block, 3).Bold && b.Caret == 3);
        });
        test("Unchanged native input does not revise the draft", () => { var b = Buffer(); b.AcceptText(b.Block.Text); Check(b.Version == 0); });
        test("Oversized native input is rejected without losing the draft", () => {
            var b = Buffer(); Throws<InvalidDataException>(() => b.AcceptText(new string('x', 2 * 1024 * 1024 + 1))); Check(b.Block.Text == "alpha beta" && b.Version == 0);
        });
        test("Invalid typing styles are rejected without content mutation", () => {
            var b = Buffer(); Throws<InvalidDataException>(() => b.Format(f => f.FontSize = float.NaN)); Check(b.Version == 0 && b.CurrentFormat.FontSize == 16);
        });
        test("Moving the caret resets a sticky typing style", () => {
            var b = Buffer(); b.Format(f => f.Bold = true); b.Select(0, 0); Check(!b.CurrentFormat.Bold);
        });
        test("Inserted links are explicit and do not leak across a link edge", () => {
            var b = Buffer(""); b.Format(f => f.Underline = true, "https://example.org"); b.ReplaceSelection("link");
            Check(b.Block.Marks.Single().Link == "https://example.org"); b.Select(4, 4); b.ReplaceSelection("x");
            Check(RichText.GetRuns(b.Block).Last().Link is null);
        });
        test("Script formatting survives typing and backup round trip", () => {
            var b = Buffer("x"); b.Format(f => f.Baseline = 1); b.ReplaceSelection("2");
            var p = new NotePage { Blocks = [b.Block] }; var roundtrip = DocumentJson.ReadPage(DocumentJson.PageJson(p));
            Check(RichText.At(roundtrip.Blocks[0], 1).Baseline == 1 && RichText.At(roundtrip.Blocks[0], 0).Baseline == 0);
        });
        test("Text flow settings are detached validated and persist in backups", () => {
            var b = Buffer(); var flow = new TextFlowSettings { LeftIndent = 24, FirstLineIndent = 12, RightIndent = 9, SpaceBefore = 6, SpaceAfter = 8, LineSpacing = 2, TabWidth = 60 };
            b.SetFlow(flow); flow.LeftIndent = 100;
            var p = DocumentJson.ReadPage(DocumentJson.PageJson(new NotePage { Blocks = [b.Block] }));
            Check(p.Blocks[0].TextFlow.LeftIndent == 24 && p.Blocks[0].TextFlow.TabWidth == 60);
            Throws<InvalidDataException>(() => b.SetFlow(new() { LineSpacing = float.PositiveInfinity })); Check(b.Block.TextFlow.LineSpacing == 2);
        });
        test("Legacy schema-one notes gain safe default flow settings", () => {
            var b = DocumentJson.ReadPage("{\"id\":\"page\",\"blocks\":[{\"id\":\"note\",\"text\":\"text\"}]} ").Blocks[0];
            Check(b.TextFlow.LineSpacing == 1.45f && b.TextFlow.LeftIndent == 0 && b.Format.Baseline == 0);
        });
        test("Invalid flow and baseline fields are rejected by import validation", () => {
            var b = new NoteBlock { TextFlow = new() { TabWidth = 0 } }; var w = new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = [new NotePage { Blocks = [b] }] }] }] };
            Throws<InvalidDataException>(() => DocumentJson.Validate(w)); b.TextFlow.TabWidth = 48; b.Format.Baseline = 2;
            Throws<InvalidDataException>(() => DocumentJson.Validate(w));
        });
        test("Paragraph formatting keeps character attributes", () => {
            var b = Buffer(); b.Select(0, 5); b.Format(f => f.Bold = true); b.FormatContainer(f => { f.Numbered = true; f.Alignment = 2; });
            Check(b.Block.Format.Alignment == 2 && b.Block.Format.Numbered && RichText.At(b.Block, 1).Bold);
        });
        test("Copying and pasting a rich fragment preserves styles and hyperlinks", () => {
            var b = Buffer(); b.Select(0, 5); b.Format(f => f.Bold = true, "https://example.org"); var slice = b.CopySelection();
            b.Select(10, 10); b.Paste(slice); Check(b.Block.Text == "alpha betaalpha" && RichText.At(b.Block, 12).Bold);
            Check(RichText.GetRuns(b.Block).Last().Link == "https://example.org");
            slice.Marks.Clear(); Check(RichText.At(b.Block, 12).Bold);
        });
        test("Pasting a fragment preserves styles before and after the replaced range", () => {
            var b = Buffer("ABCDEF"); b.Select(0, 6); b.Format(f => f.Italic = true); b.Select(2, 4);
            b.Paste(new NoteBlock { Text = "xy", Format = new() { Bold = true } });
            Check(b.Block.Text == "ABxyEF" && RichText.At(b.Block, 0).Italic && !RichText.At(b.Block, 2).Italic && RichText.At(b.Block, 2).Bold && RichText.At(b.Block, 5).Italic);
        });
        test("Malformed clipboard fragments cannot partially replace content", () => {
            var b = Buffer(); var fragment = new NoteBlock { Text = "x", Marks = [new() { Start = 3, Length = 4 }] };
            Throws<InvalidDataException>(() => b.Paste(fragment)); Check(b.Version == 0 && b.Block.Text == "alpha beta");
        });
        test("Draft commit is one undoable action and does not alias the buffer", () => {
            var note = new NoteBlock { Text = "a" }; var page = new NotePage { Blocks = [note] };
            var s = new EditorSession(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = [page] }] }] });
            var b = new TextEditingBuffer(note); b.Format(f => f.Bold = true); b.ReplaceSelection("b");
            s.EditPage(page.Id, "Rich edit", p => b.CopyContentTo(p.Blocks[0])); b.ReplaceSelection("c");
            Check(s.FindPage(page.Id)!.Blocks[0].Text == "ab"); s.Undo(); Check(s.FindPage(page.Id)!.Blocks[0].Text == "a"); s.Redo(); Check(RichText.At(s.FindPage(page.Id)!.Blocks[0], 1).Bold);
        });
        test("Copy style preserves all public character attributes", () => {
            var f = new TextFormat { Bold = true, Italic = true, Underline = true, Strike = true, Baseline = -1, Color = 0xFF123456, Highlight = 0xFFEEDD00, FontSize = 24 };
            var copy = new TextFormat(); RichText.CopyStyle(f, copy); Check(DocumentJson.CloneFormat(copy).Baseline == -1 && copy.Color == f.Color && copy.Strike);
        });
    }
}
