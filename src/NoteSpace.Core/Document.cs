using System.Text.Json;
using System.Text.Json.Serialization;

namespace NoteSpace.Core;

public enum BlockKind { Text, Heading, Checklist, Table, Image, Attachment, Divider }
public enum PaperStyle { Plain, Ruled, Grid, Dots }
public enum DrawingTool { Select, Text, Pen, Highlighter, Eraser, Rectangle, Ellipse, Line }

public sealed class Workspace
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public List<Notebook> Notebooks { get; set; } = [];
    public List<DeletedPage> Trash { get; set; } = [];
    public WorkspaceSettings Settings { get; set; } = new();
}
public sealed class WorkspaceSettings
{
    public string? SelectedPageId { get; set; }
    public bool DarkMode { get; set; }
    public bool HorizontalTabs { get; set; }
    public bool RibbonCollapsed { get; set; }
    public float Zoom { get; set; } = 1;
}
public sealed class Notebook
{
    public string Id { get; set; } = Ids.New();
    public string Title { get; set; } = "My notebook";
    public uint Color { get; set; } = 0xFF803AB3;
    public List<NoteSection> Sections { get; set; } = [];
    public List<SectionGroup> SectionGroups { get; set; } = [];
}
public sealed class NoteSection
{
    public string? GroupId { get; set; }
    public string Id { get; set; } = Ids.New();
    public string Title { get; set; } = "New section";
    public uint Color { get; set; } = 0xFF9262B8;
    public List<NotePage> Pages { get; set; } = [];
}
public sealed class NotePage
{
    public string Id { get; set; } = Ids.New();
    public string Title { get; set; } = "Untitled page";
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Modified { get; set; } = DateTimeOffset.Now;
    public int Level { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsCollapsed { get; set; }
    public PaperStyle Paper { get; set; }
    public uint PaperColor { get; set; } = 0xFFFFFFFF;
    public List<NoteBlock> Blocks { get; set; } = [];
    public List<InkStroke> Ink { get; set; } = [];
    public List<PageVersion> Versions { get; set; } = [];
}
public sealed class NoteBlock
{
    public string Id { get; set; } = Ids.New();
    public BlockKind Kind { get; set; }
    public float X { get; set; } = 48;
    public float Y { get; set; } = 140;
    public float Width { get; set; } = 520;
    public float Height { get; set; } = 100;
    public string Text { get; set; } = "";
    public TextFormat Format { get; set; } = new();
    public List<TextMark> Marks { get; set; } = [];
    public TextFlowSettings TextFlow { get; set; } = new();
    public bool Checked { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<List<string>> Cells { get; set; } = [];
    public byte[]? Data { get; set; }
    public string FileName { get; set; } = "";
    public string MediaType { get; set; } = "";
    [JsonIgnore] public NoteRect Bounds => new(X, Y, Width, Height);
}
public sealed class TextFormat
{
    public string FontFamily { get; set; } = "Segoe UI";
    public float FontSize { get; set; } = 16;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    /// <summary>-1 subscript, 0 normal, 1 superscript. FontSize remains the nominal size.</summary>
    public int Baseline { get; set; }
    public uint Color { get; set; } = 0xFF242424;
    public uint Highlight { get; set; }
    public int Alignment { get; set; }
    public bool Bullets { get; set; }
    public bool Numbered { get; set; }
}
public sealed class TextMark
{
    public int Start { get; set; }
    public int Length { get; set; }
    public TextFormat Format { get; set; } = new();
    public string? Link { get; set; }
}
public readonly record struct InkPoint(float X, float Y, float Pressure = 0.5f);
public readonly record struct NoteRect(float X, float Y, float Width, float Height)
{
    public bool Contains(float x, float y, float tolerance = 0) => x >= X - tolerance && x <= X + Width + tolerance && y >= Y - tolerance && y <= Y + Height + tolerance;
    public bool Intersects(NoteRect r) => X <= r.X + r.Width && X + Width >= r.X && Y <= r.Y + r.Height && Y + Height >= r.Y;
}
public sealed class InkStroke
{
    public string Id { get; set; } = Ids.New();
    public List<InkPoint> Points { get; set; } = [];
    public uint Color { get; set; } = 0xFF673AB7;
    public float Width { get; set; } = 3;
    public DrawingTool Tool { get; set; } = DrawingTool.Pen;
}
public sealed class DeletedPage
{
    public string SectionId { get; set; } = "";
    public DateTimeOffset Deleted { get; set; } = DateTimeOffset.Now;
    public NotePage Page { get; set; } = new();
}
public sealed class PageVersion
{
    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
    public string Description { get; set; } = "Saved version";
    public string Json { get; set; } = "";
}
public static class Ids { public static string New() => Guid.NewGuid().ToString("N"); }

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Workspace))]
[JsonSerializable(typeof(NotePage))]
[JsonSerializable(typeof(NoteBlock))]
[JsonSerializable(typeof(TextFormat))]
public partial class NoteJsonContext : JsonSerializerContext { }

public static class DocumentJson
{
    public const int MaxJsonLength = 32 * 1024 * 1024;
    public static string Serialize(Workspace value) => JsonSerializer.Serialize(value, NoteJsonContext.Default.Workspace);
    public static Workspace Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength) throw new InvalidDataException("Empty or oversized notebook file (limit: 32 MiB characters).");
        Workspace value;
        try { value = JsonSerializer.Deserialize(json, NoteJsonContext.Default.Workspace) ?? throw new InvalidDataException("No workspace in file."); }
        catch (JsonException e) { throw new InvalidDataException("The file is not valid NoteSpace JSON.", e); }
        Validate(value);
        return value;
    }
    public static Workspace Clone(Workspace value) => Deserialize(Serialize(value));
    public static string PageJson(NotePage page) => JsonSerializer.Serialize(page, NoteJsonContext.Default.NotePage);
    public static NotePage ReadPage(string json)
    {
        if (json.Length > MaxJsonLength) throw new InvalidDataException("Page version is too large.");
        var page = JsonSerializer.Deserialize(json, NoteJsonContext.Default.NotePage) ?? throw new InvalidDataException("Invalid page version.");
        Validate(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = [page] }] }] });
        return page;
    }
    public static NoteBlock CloneBlock(NoteBlock block) => JsonSerializer.Deserialize(JsonSerializer.Serialize(block, NoteJsonContext.Default.NoteBlock), NoteJsonContext.Default.NoteBlock)!;
    public static TextFormat CloneFormat(TextFormat format) => JsonSerializer.Deserialize(JsonSerializer.Serialize(format, NoteJsonContext.Default.TextFormat), NoteJsonContext.Default.TextFormat)!;
    public static void Validate(Workspace w)
    {
        if (w.SchemaVersion != 1) throw new InvalidDataException($"Unsupported NoteSpace schema {w.SchemaVersion}.");
        if (w.Notebooks is null || w.Trash is null || w.Settings is null || w.Notebooks.Count > 100 || w.Trash.Count > 2000) throw new InvalidDataException("Invalid workspace collections.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var pageCount = 0; var blockCount = 0; long points = 0;
        void Identity(string id) { if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !ids.Add(id)) throw new InvalidDataException("Missing or duplicate object identity."); }
        void Title(string title) { if (title is null || title.Length > 500) throw new InvalidDataException("Invalid title."); }
        void Format(TextFormat f) { if (f is null || f.FontFamily is null || f.FontFamily.Length > 200 || !float.IsFinite(f.FontSize) || f.FontSize < 6 || f.FontSize > 144 || f.Alignment is < 0 or > 2 || f.Baseline is < -1 or > 1) throw new InvalidDataException("Invalid text formatting."); }
        void Page(NotePage p)
        {
            if (p is null || ++pageCount > 10000) throw new InvalidDataException("Too many pages.");
            Identity(p.Id); Title(p.Title);
            if (p.Level is < 0 or > 2 || !Enum.IsDefined(p.Paper) || p.Blocks is null || p.Ink is null || p.Versions is null || p.Versions.Count > 20) throw new InvalidDataException("Invalid page.");
            foreach (var v in p.Versions) if (v is null || v.Json is null || v.Json.Length > 4 * 1024 * 1024 || v.Description is null) throw new InvalidDataException("Invalid page version.");
            foreach (var b in p.Blocks)
            {
                if (b is null || ++blockCount > 100000) throw new InvalidDataException("Too many note containers.");
                Identity(b.Id);
                if (!Enum.IsDefined(b.Kind) || !float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Width) || !float.IsFinite(b.Height) || b.X < 0 || b.Y < 0 || b.X > 100000 || b.Y > 100000 || b.Width is < 24 or > 20000 || b.Height is < 12 or > 20000) throw new InvalidDataException("Invalid note geometry.");
                if (b.Text is null || b.Text.Length > 2 * 1024 * 1024 || b.Marks is null || b.Tags is null || b.Cells is null || b.FileName is null || b.MediaType is null || b.Marks.Count > 10000 || b.Tags.Count > 100 || b.Cells.Count > 500) throw new InvalidDataException("Invalid note content.");
                Format(b.Format);
                if (b.TextFlow is null) throw new InvalidDataException("Missing text layout settings.");
                b.TextFlow.Validate();
                foreach (var m in b.Marks) { if (m is null || m.Start < 0 || m.Length < 0 || (long)m.Start + m.Length > b.Text.Length) throw new InvalidDataException("Invalid text range."); Format(m.Format); }
                foreach (var t in b.Tags) if (t is null || t.Length > 200) throw new InvalidDataException("Invalid tag.");
                foreach (var row in b.Cells) if (row is null || row.Count > 50 || row.Any(c => c is null || c.Length > 100000)) throw new InvalidDataException("Invalid table.");
                if (b.Data?.Length > 16 * 1024 * 1024) throw new InvalidDataException("Attachment exceeds 16 MiB.");
            }
            foreach (var s in p.Ink)
            {
                if (s is null || s.Points is null || s.Points.Count > 100000 || (points += s.Points.Count) > 2000000 || !float.IsFinite(s.Width) || s.Width is < 0.1f or > 100 || !Enum.IsDefined(s.Tool)) throw new InvalidDataException("Invalid ink stroke.");
                Identity(s.Id);
                foreach (var pt in s.Points) if (!float.IsFinite(pt.X) || !float.IsFinite(pt.Y) || !float.IsFinite(pt.Pressure) || Math.Abs(pt.X) > 100000 || Math.Abs(pt.Y) > 100000 || pt.Pressure is < 0 or > 1) throw new InvalidDataException("Invalid ink point.");
            }
        }
        foreach (var n in w.Notebooks)
        {
            if (n is null || n.Sections is null || n.Sections.Count > 1000) throw new InvalidDataException("Invalid notebook.");
            Identity(n.Id); Title(n.Title);
            NotebookGroups.Validate(n);
            foreach (var group in n.SectionGroups) Identity(group.Id);
            foreach (var s in n.Sections) { if (s is null || s.Pages is null) throw new InvalidDataException("Invalid section."); Identity(s.Id); Title(s.Title); foreach (var p in s.Pages) Page(p); }
        }
        foreach (var d in w.Trash) { if (d is null || d.SectionId is null) throw new InvalidDataException("Invalid recycle bin."); Page(d.Page); }
        if (!float.IsFinite(w.Settings.Zoom) || w.Settings.Zoom is < 0.25f or > 4) throw new InvalidDataException("Invalid zoom.");
    }
}
