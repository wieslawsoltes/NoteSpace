using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Rendering.Skia;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface : Grid, IDisposable
{
    private readonly NoteCanvas canvas = new();
    private readonly Canvas overlay = new();
    private readonly DispatcherTimer typingTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private EditorSession? session;
    private TextBox? editor;
    private string? editingPageId, editingBlockId;
    private string committedText = "";
    private bool editingTitle, updating, disposed, pendingText;
    private NoteBlock? copiedBlock;
    public event EventHandler? SelectionChanged;
    public event EventHandler? DraftChanged;
    public event EventHandler? ViewChanged;
    public event EventHandler<string>? Error;
    public event EventHandler<CommandRequest>? CommandRequested;
    public string? SelectedBlockId { get; private set; }
    public NoteBlock? SelectedBlock => Page?.Blocks.FirstOrDefault(b => b.Id == SelectedBlockId);
    public NotePage? Page => session?.SelectedPage;
    public DrawingTool Tool { get; set; } = DrawingTool.Select;
    public uint PenColor { get; set; } = 0xFF673AB7;
    public float PenWidth { get; set; } = 3;
    public float Zoom => canvas.Options.Zoom;
    public bool Dark { get => canvas.Options.Dark; set { canvas.Options.Dark = value; Refresh(); } }
    public bool HasPendingText => pendingText || editor is not null && editor.Text != committedText;
    public PageRenderer Renderer => canvas.Renderer;
    public EditorSession? Session
    {
        get => session;
        set
        {
            EndEditing(); if (session is not null) session.Changed -= SessionChanged;
            session = value; if (session is not null) session.Changed += SessionChanged;
            SelectedBlockId = null; Refresh();
        }
    }
    public NoteSurface()
    {
        Background = OfficeTheme.Brush(0xFFFFFFFF);
        Children.Add(canvas); Children.Add(overlay);
        ConfigureInput();
        SizeChanged += (_, _) => { PositionEditor(); canvas.Invalidate(); };
        typingTimer.Tick += (_, _) => { typingTimer.Stop(); FlushPendingText(); };
    }
    private void SessionChanged(object? sender, DocumentChange change)
    {
        if (editingPageId is not null && (Page?.Id != editingPageId || (!editingTitle && !Page.Blocks.Any(b => b.Id == editingBlockId)))) CancelEditor();
        if (SelectedBlockId is not null && SelectedBlock is null) SelectedBlockId = null;
        Refresh();
    }
    public void Refresh()
    {
        if (disposed) return;
        canvas.Page = Page; canvas.Options.SelectedId = SelectedBlockId; PositionEditor(); canvas.Invalidate();
    }
    public void NavigateToPage(string pageId, string? blockId = null)
    {
        EndEditing(); if (pendingText) return;
        CancelGesture();
        session?.SelectPage(pageId); SelectedBlockId = blockId;
        canvas.Options.OffsetX = 0; canvas.Options.OffsetY = 0;
        if (SelectedBlock is { } b) { canvas.Options.OffsetY = Math.Max(0, b.Y - 150); canvas.Options.OffsetX = Math.Max(0, b.X - 60); }
        Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SetZoom(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        value = Math.Clamp(value, 0.25f, 2.5f);
        if (Math.Abs(value - Zoom) < 0.0001f) return;
        canvas.Options.Zoom = value; Refresh(); ViewChanged?.Invoke(this, EventArgs.Empty);
    }
    public void FitWidth() { if (Page is not null) { canvas.Options.OffsetX = 0; SetZoom((float)Math.Max(200, ActualWidth - 30) / PageRenderer.Extent(Page).Width); Refresh(); } }
    public void SelectBlock(string? id)
    {
        if (id != SelectedBlockId) EndEditing(); SelectedBlockId = id; Refresh(); SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
    public NoteBlock InsertBlock(NoteBlock block, bool edit = false)
    {
        EndEditing(); if (pendingText) throw new InvalidOperationException("Resolve the current text-edit error before inserting another note.");
        if (session is null || Page is null) throw new InvalidOperationException("Select or create a page first.");
        session.AddBlock(Page.Id, block); SelectBlock(block.Id); if (edit) BeginEdit(block); return block;
    }
    public NoteBlock NewText(float? x = null, float? y = null) => InsertBlock(new NoteBlock { X = Math.Clamp(x ?? canvas.Options.OffsetX + 48, 0, 99000), Y = Math.Clamp(y ?? Math.Max(145, canvas.Options.OffsetY + 60), 120, 99000), Width = 560, Height = 60 }, true);
    public void DeleteSelected()
    {
        EndEditing(); if (pendingText || session is null || Page is null || SelectedBlockId is null) return;
        session.DeleteBlock(Page.Id, SelectedBlockId); SelectBlock(null);
    }
    public void CopySelected(bool cut = false)
    {
        FlushPendingText(); if (pendingText || SelectedBlock is not { } block) return;
        copiedBlock = DocumentJson.CloneBlock(block); if (cut) DeleteSelected();
    }
    public bool PasteSelected()
    {
        if (copiedBlock is null || Page is null) return false;
        var block = DocumentJson.CloneBlock(copiedBlock); block.Id = Ids.New(); block.X = Math.Min(99000, block.X + 24); block.Y = Math.Min(99000, block.Y + 24);
        InsertBlock(block); copiedBlock = DocumentJson.CloneBlock(block); return true;
    }
    public void Dispose()
    {
        if (disposed) return; EndEditing(); disposed = true; typingTimer.Stop();
        if (session is not null) session.Changed -= SessionChanged; canvas.Dispose();
    }
}
