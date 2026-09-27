namespace NoteSpace.Core;

public static class SampleWorkspace
{
    public static Workspace Create()
    {
        var welcome = new NotePage { Title = "Welcome to NoteSpace", Blocks = [
            Text("A little space for your big ideas.", 48, 145, 600, 52, 27, true, 0xFF7030A0),
            Text("Capture a thought. Sketch a possibility. Make a plan.\nYour notebooks, sections, and pages keep everything together.", 48, 212, 610, 80),
            Text("Make yourself at home", 48, 320, 540, 42, 21, true),
            new NoteBlock { Kind = BlockKind.Checklist, Text = "Create a page with the + Add page button", X = 48, Y = 374, Width = 580, Height = 42 },
            new NoteBlock { Kind = BlockKind.Checklist, Text = "Double-click anywhere on the page to start a note", X = 48, Y = 426, Width = 580, Height = 42 },
            new NoteBlock { Kind = BlockKind.Checklist, Text = "Try a pen, a highlighter, or a shape on the Draw tab", X = 48, Y = 478, Width = 580, Height = 42 },
            Text("YOUR NOTES, YOUR SPACE", 710, 164, 350, 40, 12, true, 0xFF7030A0),
            Text("Everything saves in this browser.\nExport a notebook backup from File to keep a copy you control.", 710, 215, 350, 124),
            Text("A fresh page is a fresh start.", 710, 390, 350, 72, 22, false, 0xFF9863BC)
        ] };
        var meeting = new NotePage { Title = "Weekly planning", Blocks = [
            Text("This week’s focus", 48, 145, 600, 48, 24, true),
            Text("Build something worth sharing.\nKeep the important things visible.", 48, 210, 650, 80),
            new NoteBlock { Kind = BlockKind.Table, X = 48, Y = 330, Width = 680, Height = 172, Cells = [["Priority", "Owner", "Status"], ["Design review", "Team", "In progress"], ["Prototype", "You", "Ready"], ["Share feedback", "Everyone", "Next"]] }
        ] };
        var ideas = new NotePage { Title = "Ideas & inspiration", Paper = PaperStyle.Dots, Blocks = [Text("What could we make possible?", 48, 145, 630, 60, 26, true, 0xFF7030A0), Text("Every great idea starts with a small note.\nDraw, write, rearrange, and connect your thoughts.", 48, 235, 640, 100)] };
        var n = new Notebook { Title = "My notebook", Sections = [
            new NoteSection { Title = "Getting started", Color = 0xFF9063B8, Pages = [welcome, meeting, new NotePage { Title = "Quick notes", Blocks = [Text("Things to remember…", 48, 145, 600, 100)] }] },
            new NoteSection { Title = "Projects", Color = 0xFF4B96C8, Pages = [ideas] },
            new NoteSection { Title = "Personal", Color = 0xFF6CAA71, Pages = [new NotePage { Title = "Reading list", Blocks = [new NoteBlock { Kind = BlockKind.Checklist, Text = "Add the next book you want to read", X = 48, Y = 145, Width = 600, Height = 48 }] }] }
        ] };
        return new Workspace { Notebooks = [n], Settings = new WorkspaceSettings { SelectedPageId = welcome.Id } };
    }
    public static NoteBlock Text(string text, float x, float y, float width, float height, float size = 16, bool bold = false, uint color = 0xFF242424) => new() { Text = text, X = x, Y = y, Width = width, Height = height, Format = new TextFormat { FontSize = size, Bold = bold, Color = color } };
}
