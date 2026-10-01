using NoteSpace.Core;
using NoteSpace.Editor;

internal static class NativeDraftTests
{
    private static void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static void Run(Action<string, Action> test)
    {
        test("Repeated native insertion styles the selected offset rather than the last match", () => {
            var edit = new TextEditingBuffer(new NoteBlock { Text = "aaaa" });
            edit.Select(1, 1); edit.Format(f => f.Bold = true); edit.AcceptText("aaaaa");
            Check(edit.Caret == 2 && RichText.At(edit.Block, 1).Bold);
            Check(!RichText.At(edit.Block, 0).Bold && !RichText.At(edit.Block, 2).Bold && !RichText.At(edit.Block, 4).Bold);
        });
        test("Repeated native replacement keeps styles outside the actual selection", () => {
            var note = new NoteBlock { Text = "aaaa" }; RichText.Apply(note, 3, 1, f => f.Italic = true);
            var edit = new TextEditingBuffer(note); edit.Select(1, 3); edit.Format(f => f.Bold = true);
            edit.AcceptText("aaa");
            Check(edit.Caret == 2 && edit.Block.Text == "aaa" && RichText.At(edit.Block, 1).Bold && RichText.At(edit.Block, 2).Italic);
        });
        test("Repeated native deletion removes the selected graphemes", () => {
            var note = new NoteBlock { Text = "😀😀😀" }; RichText.Apply(note, 4, 2, f => f.Italic = true);
            var edit = new TextEditingBuffer(note); edit.Select(0, 2); edit.AcceptText("😀😀");
            Check(edit.Caret == 0 && !RichText.At(edit.Block, 0).Italic && RichText.At(edit.Block, 2).Italic);
        });
        test("Native reconciliation falls back when input is unrelated to the selection", () => {
            var note = new NoteBlock { Text = "one two" }; RichText.Apply(note, 4, 3, f => f.Italic = true);
            var edit = new TextEditingBuffer(note); edit.AcceptText("ONE two");
            Check(edit.Block.Text == "ONE two" && edit.Caret == 3 && RichText.At(edit.Block, 5).Italic);
        });
        test("Native insertion at every repeated-text boundary retains exact formatting", () => {
            for (var at = 0; at <= 24; at++)
            {
                var edit = new TextEditingBuffer(new NoteBlock { Text = new string('a', 24) });
                edit.Select(at, at); edit.Format(f => f.Underline = true); edit.AcceptText(new string('a', 25));
                Check(edit.Caret == at + 1);
                for (var i = 0; i < 25; i++) Check(RichText.At(edit.Block, i).Underline == (i == at), $"insertion {at}, position {i}");
            }
        });
        test("Selection attribute cache invalidates after formatting typing and paste", () => {
            var edit = new TextEditingBuffer(new NoteBlock { Text = "abc" }); edit.Select(0, 3);
            Check(!edit.AllHave(f => f.Bold)); edit.Format(f => f.Bold = true); Check(edit.AllHave(f => f.Bold));
            edit.Select(3, 3); edit.Format(f => f.Bold = false); edit.ReplaceSelection("x"); edit.Select(0, 4); Check(!edit.AllHave(f => f.Bold));
            edit.Paste(new NoteBlock { Text = "z", Format = new() { Bold = true } }); edit.Select(0, 1); Check(edit.AllHave(f => f.Bold));
        });
        test("Warm selection attribute queries do not allocate per ribbon button", () => {
            var edit = new TextEditingBuffer(new NoteBlock { Text = "abc" }); edit.Select(0, 3); edit.Format(f => f.Bold = true);
            Func<TextFormat, bool> bold = static f => f.Bold;
            for (var i = 0; i < 100; i++) Check(edit.AllHave(bold));
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 1000; i++) Check(edit.AllHave(bold));
            Check(GC.GetAllocatedBytesForCurrentThread() - before < 1024);
        });
        test("Newline normalization preserves styles across separate paragraphs", () => {
            var note = new NoteBlock { Text = "a\r\nb\rc\n" };
            RichText.Apply(note, 3, 1, f => f.Bold = true); RichText.Apply(note, 5, 1, f => f.Italic = true);
            var edit = new TextEditingBuffer(note); edit.Select(6, 3); edit.NormalizeLineEndings();
            Check(edit.Block.Text == "a\nb\nc\n" && edit.Anchor == 5 && edit.Caret == 2);
            Check(RichText.At(edit.Block, 2).Bold && RichText.At(edit.Block, 4).Italic && !RichText.At(edit.Block, 0).Bold);
            var version = edit.Version; edit.NormalizeLineEndings(); Check(edit.Version == version);
        });
        test("Null replacement and attribute predicates cannot damage a draft", () => {
            var edit = new TextEditingBuffer(new NoteBlock { Text = "abc" });
            Throws<ArgumentNullException>(() => edit.ReplaceSelection(null!));
            Throws<ArgumentNullException>(() => edit.AllHave(null!));
            Check(edit.Version == 0 && edit.Block.Text == "abc");
        });
    }
}
