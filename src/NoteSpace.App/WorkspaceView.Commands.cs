using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private async Task ExecuteCommandAsync(string command, string? entityId)
    {
        var pageId = session.FindPage(entityId)?.Id ?? CurrentPage?.Id;
        switch (command)
        {
            case "file": surface.EndEditing(); backstage.Show(session.Document, theme); return;
            case "save": await SaveAsync(); return;
            case "export": case "markdown": case "html": case "png": await ExportAsync(command); return;
            case "import": await ImportAsync(); return;
            case "undo": surface.EndEditing(); session.Undo(); BindNavigation(); return;
            case "redo": surface.EndEditing(); session.Redo(); BindNavigation(); return;
            case "new-page":
                surface.EndEditing();
                var destination = session.FindSection(entityId) ?? CurrentSection;
                if (destination is null) { var notebook = session.AddNotebook("My notebook"); Navigate(notebook.Sections[0].Pages[0].Id); }
                else Navigate(session.AddPage(destination.Id).Id);
                surface.BeginEditTitle(); return;
            case "new-text": surface.Tool = DrawingTool.Select; surface.NewText(); return;
            case "cut": surface.CopySelected(true); return;
            case "copy": surface.CopySelected(); return;
            case "paste":
                if (!surface.PasteSelected()) await MessageAsync("Paste text or notes", "Copy a note container with Home → Copy, then paste it here. Inside a text container, use the native Ctrl+V or ⌘V shortcut to paste text from another application.");
                return;
            case "delete-block": surface.DeleteSelected(); return;
            case "bold": Format(f => f.Bold = !f.Bold); return;
            case "italic": Format(f => f.Italic = !f.Italic); return;
            case "underline": Format(f => f.Underline = !f.Underline); return;
            case "strike": Format(f => f.Strike = !f.Strike); return;
            case "font-up": Format(f => f.FontSize = Math.Min(144, f.FontSize + 2)); return;
            case "font-down": Format(f => f.FontSize = Math.Max(6, f.FontSize - 2)); return;
            case "font":
                var font = await FontDialogAsync(surface.SelectedBlock?.Format ?? new TextFormat());
                if (font is not null) Format(f => { f.FontFamily = font.Value.Family; f.FontSize = font.Value.Size; }); return;
            case "text-color":
                var color = await ColorDialogAsync("Font color", surface.SelectedBlock?.Format.Color ?? 0xFF242424);
                if (color is not null) Format(f => f.Color = color.Value); return;
            case "text-highlight": Format(f => f.Highlight = f.Highlight == 0 ? 0xFFFFE77A : 0); return;
            case "clear-format":
                EditSelected("Clear formatting", b => { b.Format = new TextFormat(); b.Marks.Clear(); }); return;
            case "bullets": Format(f => { f.Bullets = !f.Bullets; f.Numbered = false; }); return;
            case "numbered": Format(f => { f.Numbered = !f.Numbered; f.Bullets = false; }); return;
            case "align-left": Format(f => f.Alignment = 0); return;
            case "align-center": Format(f => f.Alignment = 1); return;
            case "align-right": Format(f => f.Alignment = 2); return;
            case "style-heading": Format(f => { f.FontSize = 26; f.Bold = true; f.Color = 0xFF7030A0; }); return;
            case "style-subheading": Format(f => { f.FontSize = 21; f.Bold = true; f.Color = 0xFF444444; }); return;
            case "style-normal": Format(f => { f.FontSize = 16; f.Bold = false; f.Italic = false; f.Color = 0xFF242424; }); return;
            case "todo":
                if (surface.SelectedBlock is null) surface.InsertBlock(new NoteBlock { Kind = BlockKind.Checklist, Text = "New task", Height = 48 }, true);
                else EditSelected("To-do tag", b => b.Kind = b.Kind == BlockKind.Checklist ? BlockKind.Text : BlockKind.Checklist);
                return;
            case "important": ToggleTag("Important"); return;
            case "question": ToggleTag("Question"); return;
            case "find-tags": await FindTagsAsync(); return;
            case "search": searchOpen = true; ApplyLayout(); search.QueryBox.Focus(FocusState.Programmatic); return;
            case "replace": await ReplaceTextAsync(); return;
            case "table": await TableDialogAsync(null); return;
            case "edit-table": await TableDialogAsync(entityId); return;
            case "image": await InsertFileAsync(true); return;
            case "attachment": await InsertFileAsync(false); return;
            case "save-attachment":
                var attachment = CurrentPage?.Blocks.FirstOrDefault(b => b.Id == entityId);
                if (attachment?.Data is not null) await platform.SaveFileAsync(attachment.FileName, attachment.Data, attachment.MediaType); return;
            case "link":
                var url = await PromptAsync("Insert hyperlink", "https://", "Web address or mailto: address");
                if (url is not null)
                {
                    if (!NoteSpace.Storage.NoteExport.SafeLink(url)) throw new InvalidDataException("Use an absolute https:, http:, or mailto: link.");
                    if (surface.SelectedBlock is null) surface.NewText();
                    surface.FormatSelection(f => { f.Underline = true; f.Color = 0xFF2868B2; }, url);
                    // When no range was selected, insert an explicit, readable link.
                    if (surface.SelectedBlock?.Text.Length == 0) surface.InsertText(url);
                }
                return;
            case "date": surface.InsertText(DateTime.Now.ToString("D")); return;
            case "time": surface.InsertText(DateTime.Now.ToString("t")); return;
            case "datetime": surface.InsertText(DateTime.Now.ToString("f")); return;
            case "divider": surface.InsertBlock(new NoteBlock { Kind = BlockKind.Divider, Y = NextY(), Height = 24, Width = 640 }); return;
            case "symbol":
                var symbol = await PromptAsync("Insert symbol", "✓", "Examples: ✓ → ★ © Ω π ∑ ± ∞"); if (symbol is not null) surface.InsertText(symbol); return;
            case "template": await TemplateAsync(); return;
            case "select": SetTool(DrawingTool.Select); return;
            case "type": SetTool(DrawingTool.Text); return;
            case "pen": SetTool(DrawingTool.Pen); return;
            case "highlighter": SetTool(DrawingTool.Highlighter); if (surface.PenColor == 0xFF673AB7) surface.PenColor = 0xFFFFD94A; return;
            case "eraser": SetTool(DrawingTool.Eraser); return;
            case "rectangle": SetTool(DrawingTool.Rectangle); return;
            case "ellipse": SetTool(DrawingTool.Ellipse); return;
            case "line": SetTool(DrawingTool.Line); return;
            case "ink-color":
                var inkColor = await ColorDialogAsync("Ink color", surface.PenColor); if (inkColor is not null) surface.PenColor = inkColor.Value; return;
            case "ink-width":
                var width = await PromptAsync("Pen thickness", surface.PenWidth.ToString(System.Globalization.CultureInfo.InvariantCulture), "0.5 to 30 pixels");
                if (width is not null) { if (!float.TryParse(width, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value) || value is < 0.5f or > 30) throw new InvalidDataException("Enter a thickness between 0.5 and 30."); surface.PenWidth = value; } return;
            case "clear-ink":
                if (pageId is not null && await ConfirmAsync("Clear ink?", "Remove all ink and shapes from this page? You can undo this action.", "Clear ink")) session.EditPage(pageId, "Clear ink", p => p.Ink.Clear()); return;
            case "save-version": if (pageId is not null) { surface.FlushPendingText(); session.SaveVersion(pageId); saveStatus = "Page version saved"; } return;
            case "versions": await VersionsAsync(pageId); return;
            case "trash": await RecycleBinAsync(); return;
            case "recent": recentSort = !recentSort; BindNavigation(); return;
            case "word-count":
                var text = string.Join(" ", CurrentPage?.Blocks.Select(b => b.Text + " " + string.Join(" ", b.Cells.SelectMany(r => r))) ?? []);
                await MessageAsync("Page statistics", $"{Regex.Matches(text, @"\S+").Count:N0} words\n{text.Length:N0} characters\n{CurrentPage?.Blocks.Count ?? 0} note containers\n{CurrentPage?.Ink.Count ?? 0} ink strokes and shapes"); return;
            case "favorite": if (pageId is not null) session.EditPage(pageId, "Favorite page", p => p.IsFavorite = !p.IsFavorite, true); return;
            case "paper-plain": SetPaper(PaperStyle.Plain); return;
            case "paper-ruled": SetPaper(PaperStyle.Ruled); return;
            case "paper-grid": SetPaper(PaperStyle.Grid); return;
            case "paper-dots": SetPaper(PaperStyle.Dots); return;
            case "paper-color":
                var paperColor = await ColorDialogAsync("Page color", CurrentPage?.PaperColor ?? 0xFFFFFFFF);
                if (paperColor is not null && pageId is not null) session.EditPage(pageId, "Page color", p => p.PaperColor = paperColor.Value); return;
            case "zoom-out": surface.SetZoom(surface.Zoom - 0.1f); return;
            case "zoom-in": surface.SetZoom(surface.Zoom + 0.1f); return;
            case "zoom-reset": surface.SetZoom(1); return;
            case "fit-width": surface.FitWidth(); return;
            case "full-page": focusMode = !focusMode; ApplyLayout(); return;
            case "navigation": navigationOpen = !navigationOpen; ApplyLayout(); return;
            case "horizontal-tabs": session.Document.Settings.HorizontalTabs = !session.Document.Settings.HorizontalTabs; MarkDirty(); ApplyLayout(); return;
            case "dark-mode": session.Document.Settings.DarkMode = !session.Document.Settings.DarkMode; MarkDirty(); ApplyTheme(); return;
            case "collapse-ribbon": session.Document.Settings.RibbonCollapsed = !session.Document.Settings.RibbonCollapsed; MarkDirty(); ApplyTheme(); return;
            case "about": await MessageAsync("NoteSpace 0.1.0", "An independent notebook workspace built with Uno Platform 6.7.30, .NET 10, and SkiaSharp 3.119.2.\n\nMIT licensed. No account required. No notebook content is sent to a server.\n\nThis is not Microsoft OneNote and does not support .one files, OneDrive, OCR, audio recording, or collaborative sync. Full OneNote parity is an ongoing goal, not a current claim."); return;
            case "shortcuts": await MessageAsync("Make yourself at home", "Double-click a page title or note to edit. Double-click empty paper to add a text container. Drag a container’s top grip to move it, or its bottom-right corner to resize.\n\nDraw: choose a pen, highlighter, eraser, or shape. Wheel to scroll; middle-drag to pan.\n\nCtrl+S saves. Ctrl+F searches. Ctrl+Z / Ctrl+Y undo and redo. Ctrl+Alt+N adds a page. Ctrl+B / I / U format text.\n\nRight-click a page or section for more actions. Export a .notespace backup regularly."); return;
            default: await ManageAsync(command, entityId); return;
        }
    }
    private float NextY() => Math.Min(99000, Math.Max(145, (CurrentPage?.Blocks.Select(b => b.Y + b.Height).DefaultIfEmpty(120).Max() ?? 120) + 24));
    private void Format(Action<TextFormat> action)
    {
        if (surface.SelectedBlock is null) surface.NewText(); surface.FormatSelection(action);
    }
    private void EditSelected(string label, Action<NoteBlock> edit)
    {
        surface.FlushPendingText(); if (surface.SelectedBlock is not { } block || CurrentPage is null) return;
        session.EditPage(CurrentPage.Id, label, p => { var b = p.Blocks.First(b => b.Id == block.Id); edit(b); b.Height = Math.Max(b.Height, surface.Renderer.MeasureHeight(b)); }); surface.Refresh();
    }
    private void ToggleTag(string tag) => EditSelected("Toggle " + tag + " tag", b => { if (!b.Tags.Remove(tag)) b.Tags.Add(tag); });
    private void SetTool(DrawingTool tool) { surface.EndEditing(); surface.Tool = tool; UpdateStatus(); }
    private void SetPaper(PaperStyle paper) { if (CurrentPage is not null) session.EditPage(CurrentPage.Id, "Paper style", p => p.Paper = paper); }
}
