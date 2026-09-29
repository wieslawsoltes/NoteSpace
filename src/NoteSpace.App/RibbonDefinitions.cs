using NoteSpace.Controls;
namespace NoteSpace.App;

internal static class RibbonDefinitions
{
    private static RibbonCommand C(string id, string label, string icon, bool small = false, string? hint = null) => new(id, label, icon, hint, small);
    public static IReadOnlyList<RibbonTab> Create() => [
        new("Home", [
            new("Clipboard", [C("paste", "Paste", "clipboard", hint: "Paste a copied note container; use Ctrl+V inside text"), C("cut", "Cut", "cut", true), C("copy", "Copy", "copy", true), C("delete-block", "Delete", "trash", true)]),
            new("Basic Text", [C("font", "Segoe UI  ·  Size", "text", true), C("bold", "Bold", "B", true), C("italic", "Italic", "I", true), C("font-up", "Grow font", "A", true), C("underline", "Underline", "U", true), C("strike", "Strikethrough", "S", true), C("font-down", "Shrink font", "A", true), C("text-color", "Font color", "color", true), C("text-highlight", "Highlight", "highlighter", true)]),
            new("Paragraph", [C("bullets", "Bullets", "bullets", true), C("numbered", "Numbering", "numbered", true), C("align-left", "Align left", "align-left", true), C("align-center", "Center", "align-center", true), C("align-right", "Align right", "align-right", true), C("clear-format", "Clear format", "eraser", true)]),
            new("Styles", [C("style-heading", "Heading 1", "text", true), C("style-subheading", "Heading 2", "text", true), C("style-normal", "Normal", "text", true)]),
            new("Tags", [C("todo", "To Do Tag", "check"), C("important", "Important", "star", true), C("question", "Question", "?", true), C("find-tags", "Find Tags", "tag", true)]),
            new("Editing", [C("search", "Find", "search"), C("replace", "Replace", "redo", true), C("new-text", "Text container", "add", true)])
        ]),
        new("Insert", [
            new("Notes", [C("new-page", "New Page", "page"), C("new-text", "Text Box", "text"), C("template", "Templates", "folder")]),
            new("Organization", [C("new-section", "Section", "folder", true), C("new-section-group", "Section Group", "folder", true)]),
            new("Tables", [C("table", "Table", "table")]),
            new("Files", [C("image", "Pictures", "image"), C("attachment", "File Attachment", "attachment")]),
            new("Links", [C("link", "Link", "link")]),
            new("Time Stamp", [C("date", "Date", "calendar", true), C("time", "Time", "history", true), C("datetime", "Date & Time", "calendar", true)]),
            new("More", [C("divider", "Divider", "line"), C("symbol", "Symbols", "Ω")])
        ]),
        new("Draw", [
            new("Tools", [C("select", "Select", "select"), C("type", "Type", "text"), C("eraser", "Eraser", "eraser")]),
            new("Pens", [C("pen", "Pen", "pen"), C("highlighter", "Highlighter", "highlighter"), C("ink-color", "Color", "color"), C("ink-width", "Thickness", "line")]),
            new("Shapes", [C("rectangle", "Rectangle", "rectangle"), C("ellipse", "Ellipse", "ellipse"), C("line", "Line", "line")]),
            new("Edit", [C("undo", "Undo", "undo"), C("redo", "Redo", "redo"), C("clear-ink", "Clear Ink", "trash")])
        ]),
        new("History", [
            new("Changes", [C("undo", "Undo", "undo"), C("redo", "Redo", "redo")]),
            new("Page Versions", [C("save-version", "Save Version", "save"), C("versions", "Page Versions", "history")]),
            new("Notebooks", [C("recent", "Recent Edits", "history"), C("trash", "Recycle Bin", "trash")])
        ]),
        new("Review", [
            new("Find", [C("search", "Search", "search"), C("replace", "Replace", "redo"), C("find-tags", "Find Tags", "tag")]),
            new("Page", [C("word-count", "Word Count", "text"), C("favorite", "Favorite", "star"), C("rename-page", "Rename", "page")])
        ]),
        new("View", [
            new("Views", [C("full-page", "Full Page", "fullscreen"), C("horizontal-tabs", "Tabs Layout", "folder"), C("dark-mode", "Dark Mode", "color")]),
            new("Page Setup", [C("paper-plain", "Plain", "page", true), C("paper-ruled", "Rule Lines", "ruled", true), C("paper-grid", "Grid Lines", "grid", true), C("paper-dots", "Dot Grid", "grid", true), C("paper-color", "Page Color", "color", true)]),
            new("Zoom", [C("zoom-out", "Zoom Out", "−"), C("zoom-in", "Zoom In", "+"), C("zoom-reset", "100%", "text"), C("fit-width", "Page Width", "fullscreen")]),
            new("Window", [C("collapse-ribbon", "Ribbon", "chevron"), C("navigation", "Navigation", "menu")])
        ]),
        new("Table", [
            new("Cells", [C("table-edit-cell", "Edit Cell", "text"), C("table-clear-cell", "Clear Cell", "eraser"), C("table-edit-all", "Whole Table", "table")]),
            new("Rows", [C("table-row-above", "Insert Above", "add", true), C("table-row-below", "Insert Below", "add", true), C("table-delete-row", "Delete Row", "trash", true)]),
            new("Columns", [C("table-column-left", "Insert Left", "add", true), C("table-column-right", "Insert Right", "add", true), C("table-delete-column", "Delete Column", "trash", true)]),
            new("Data", [C("table-copy", "Copy Table", "copy"), C("table-paste", "Paste Cells", "clipboard"), C("table-transpose", "Transpose", "table")])
        ]),
        new("Layout", [
            new("Character", [C("superscript", "Superscript", "text", true), C("subscript", "Subscript", "text", true), C("clear-format", "Clear format", "eraser", true)]),
            new("Format Painter", [C("copy-format", "Copy Format", "copy"), C("paste-format", "Paste Format", "clipboard")]),
            new("Indent", [C("indent-more", "Increase Indent", "align-right", true), C("indent-less", "Decrease Indent", "align-left", true), C("text-layout", "Text Layout…", "text", true)]),
            new("Line Spacing", [C("spacing-single", "Single", "text", true), C("spacing-normal", "Normal", "text", true), C("spacing-double", "Double", "text", true)]),
            new("Container", [C("container-layout", "Size & Position", "rectangle"), C("bring-forward", "Bring Forward", "page", true), C("send-backward", "Send Backward", "page", true)])
        ]),
        new("Help", [new("NoteSpace", [C("shortcuts", "Get Started", "book"), C("about", "About", "?"), C("export", "Back Up", "download")])])
    ];
}
