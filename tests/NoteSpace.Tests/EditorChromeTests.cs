using NoteSpace.Core;
using NoteSpace.Editor;

internal static class EditorChromeTests
{
    private static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static void Run(Action<string, Action> test)
    {
        test("Classic ribbon and enabled selection tools are backward-compatible defaults", () => {
            var w = DocumentJson.Deserialize("{\"schemaVersion\":1}");
            Check(!w.Settings.Navigation.SimplifiedRibbon && w.Settings.Navigation.ShowSelectionToolbar);
        });
        test("Ribbon and toolbar preferences survive independent backup cloning", () => {
            var w = new Workspace(); w.Settings.Navigation.SimplifiedRibbon = true; w.Settings.Navigation.ShowSelectionToolbar = false;
            var clone = DocumentJson.Clone(w); w.Settings.Navigation.SimplifiedRibbon = false;
            Check(clone.Settings.Navigation.SimplifiedRibbon && !clone.Settings.Navigation.ShowSelectionToolbar);
        });
        test("A full command strip does not reserve unnecessary overflow space", () => {
            Check(EditorChromeLayout.VisibleCommands([30, 40, 50], 120) == 3);
            Check(EditorChromeLayout.VisibleCommands([], 0) == 0);
        });
        test("Overflow command layout preserves a maximal leading command sequence", () => {
            Check(EditorChromeLayout.VisibleCommands([40, 80, 40, 40], 180) == 2);
            Check(EditorChromeLayout.VisibleCommands([40, 80, 40, 40], 80) == 1);
            Check(EditorChromeLayout.VisibleCommands([40, 80], 39) == 0);
        });
        test("Invalid strip geometry is rejected before calculating visibility", () => {
            Throws<ArgumentOutOfRangeException>(() => EditorChromeLayout.VisibleCommands([double.NaN], 300));
            Throws<ArgumentOutOfRangeException>(() => EditorChromeLayout.VisibleCommands([30], double.PositiveInfinity));
            Throws<ArgumentOutOfRangeException>(() => EditorChromeLayout.VisibleCommands([-1], 300));
        });
        test("Random command layouts always fit including their overflow affordance", () => {
            var random = new Random(287);
            for (var i = 0; i < 300; i++)
            {
                var widths = Enumerable.Range(0, random.Next(1, 60)).Select(_ => (double)random.Next(20, 150)).ToArray();
                var space = random.Next(40, 2000); var n = EditorChromeLayout.VisibleCommands(widths, space);
                Check(n >= 0 && n <= widths.Length);
                Check(widths.Take(n).Sum() + (n < widths.Length ? 40 : 0) <= space);
                if (n < widths.Length) Check(widths.Take(n + 1).Sum() + 40 > space);
            }
        });
        test("Selection tools prefer space above the selected text", () => {
            var rect = EditorChromeLayout.PlaceToolbar(new(120, 200, 80, 30), 800, 600)!.Value;
            Check(rect.X == 120 && rect.Y == 154 && rect.Y + rect.Height < 200);
        });
        test("Top-edge selection tools move below rather than covering the text", () => {
            var rect = EditorChromeLayout.PlaceToolbar(new(20, 8, 80, 24), 800, 600)!.Value;
            Check(rect.Y == 38);
        });
        test("Selection tools clamp horizontally at the viewport edge", () => {
            var right = EditorChromeLayout.PlaceToolbar(new(760, 150, 100, 25), 800, 600)!.Value;
            var left = EditorChromeLayout.PlaceToolbar(new(-15, 150, 100, 25), 800, 600)!.Value;
            Check(right.X + right.Width == 792 && left.X == 8);
        });
        test("Offscreen selections and insufficient space hide the toolbar", () => {
            Check(EditorChromeLayout.PlaceToolbar(new(900, 200, 20, 30), 800, 600) is null);
            Check(EditorChromeLayout.PlaceToolbar(new(20, -50, 20, 30), 800, 600) is null);
            Check(EditorChromeLayout.PlaceToolbar(new(20, 12, 100, 40), 800, 70) is null);
            Check(EditorChromeLayout.PlaceToolbar(new(20, 100, 80, 30), 200, 500) is null);
        });
        test("Toolbar placement rejects invalid view and selection dimensions", () => {
            Throws<ArgumentOutOfRangeException>(() => EditorChromeLayout.PlaceToolbar(new(float.NaN, 1, 2, 3), 800, 600));
            Throws<ArgumentOutOfRangeException>(() => EditorChromeLayout.PlaceToolbar(new(1, 1, -2, 3), 800, 600));
            Throws<ArgumentOutOfRangeException>(() => EditorChromeLayout.PlaceToolbar(new(1, 1, 2, 3), 800, 600, height: 0));
        });
        test("Toolbar geometry does not change selection or document state", () => {
            var block = new NoteBlock { Text = "selected text" }; var draft = new TextEditingBuffer(block); draft.Select(0, 8);
            for (var i = 0; i < 100; i++) _ = EditorChromeLayout.PlaceToolbar(new(i, 200, 80, 30), 800, 600);
            Check(draft.Version == 0 && draft.SelectionStart == 0 && draft.SelectionLength == 8 && block.Text == "selected text");
        });
    }
}
