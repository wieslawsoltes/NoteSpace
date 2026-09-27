using NoteSpace.Controls;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private async Task ManageAsync(string command, string? entityId)
    {
        surface.EndEditing();
        if (surface.HasPendingText) throw new InvalidOperationException("Resolve the current text edit before changing notebook organization.");
        if (command is "new-section-group" or "rename-group" or "collapse-group" or "expand-group" or "move-group" or "move-section" or "group-up" or "group-down" or "ungroup")
        { await ManageGroupsAsync(command, entityId); return; }
        var pageId = session.FindPage(entityId)?.Id ?? CurrentPage?.Id;
        var selectedSection = session.FindSection(entityId) ?? CurrentSection;
        switch (command)
        {
            case "new-notebook":
                var name = await PromptAsync("New notebook", "My notebook");
                if (name is not null) { var n = session.AddNotebook(name); Navigate(n.Sections[0].Pages[0].Id); } return;
            case "new-section":
                var parentGroup = session.FindSectionGroup(entityId);
                var owner = session.Document.Notebooks.FirstOrDefault(n => n.Id == entityId || n.SectionGroups.Any(g => g.Id == entityId))
                    ?? session.Pages.FirstOrDefault(p => p.Page.Id == pageId).Notebook ?? session.Document.Notebooks.FirstOrDefault();
                if (owner is null) { await ManageAsync("new-notebook", null); return; }
                var sectionName = await PromptAsync("New section", "New section");
                if (sectionName is not null) Navigate(session.AddSectionInGroup(owner.Id, sectionName, parentGroup?.Id).Pages[0].Id); return;
            case "rename-notebook":
                var notebook = session.Document.Notebooks.FirstOrDefault(n => n.Id == entityId); if (notebook is null) return;
                var notebookName = await PromptAsync("Rename notebook", notebook.Title);
                if (notebookName is not null) session.Execute("Rename notebook", _ => notebook.Title = EditorSession.CleanTitle(notebookName), true); return;
            case "rename-section":
                if (selectedSection is null) return;
                var renamedSection = await PromptAsync("Rename section", selectedSection.Title);
                if (renamedSection is not null) session.Execute("Rename section", _ => selectedSection.Title = EditorSession.CleanTitle(renamedSection), true); return;
            case "section-color":
                if (selectedSection is null) return;
                var color = await ColorDialogAsync("Section color", selectedSection.Color);
                if (color is not null) session.Execute("Section color", _ => selectedSection.Color = color.Value, true); return;
            case "section-up": case "section-down":
                if (selectedSection is null) return;
                session.MoveSectionSibling(selectedSection.Id, command == "section-up" ? -1 : 1); return;
            case "delete-section":
                if (selectedSection is null || !await ConfirmAsync("Delete section?", $"Move all pages in “{selectedSection.Title}” to the notebook recycle bin?", "Delete")) return;
                session.Execute("Delete section", w => {
                    foreach (var page in selectedSection.Pages) w.Trash.Add(new DeletedPage { SectionId = selectedSection.Id, Page = page });
                    foreach (var n in w.Notebooks) n.Sections.Remove(selectedSection);
                    w.Settings.SelectedPageId = session.Pages.FirstOrDefault().Page?.Id;
                }, true); return;
            case "delete-notebook":
                var removing = session.Document.Notebooks.FirstOrDefault(n => n.Id == entityId);
                if (removing is null || !await ConfirmAsync("Delete notebook?", $"Remove “{removing.Title}”? Its pages will remain in the recycle bin. Export a backup first to preserve the organization.", "Delete")) return;
                session.Execute("Delete notebook", w => {
                    foreach (var s in removing.Sections) foreach (var p in s.Pages) w.Trash.Add(new DeletedPage { SectionId = s.Id, Page = p });
                    w.Notebooks.Remove(removing); w.Settings.SelectedPageId = session.Pages.FirstOrDefault().Page?.Id;
                }, true); return;
            case "rename-page":
                if (pageId is null) return;
                var title = await PromptAsync("Rename page", session.FindPage(pageId)!.Title);
                if (title is not null) session.RenamePage(pageId, title); return;
            case "duplicate-page": if (pageId is not null) Navigate(session.DuplicatePage(pageId).Id); return;
            case "delete-page":
                if (pageId is not null && await ConfirmAsync("Delete page?", "The page will move to the notebook recycle bin. Its subpages will stay in the section and move up one level. You can restore the page from History.", "Delete")) session.DeletePage(pageId); return;
            case "move-page":
                if (pageId is null) return;
                var destinations = session.Document.Notebooks.SelectMany(n => n.Sections.Select(s => (Notebook: n, Section: s))).ToList();
                var targetIndex = await ChooseAsync("Move page to section", destinations.Select(x => NotebookGroups.Path(x.Notebook, x.Section.GroupId) + " / " + x.Section.Title).ToList());
                if (targetIndex is not null) { var target = destinations[targetIndex.Value].Section; session.MovePage(pageId, target.Id, target.Pages.Count); Navigate(pageId); } return;
            case "new-subpage":
                if (pageId is not null) { Navigate(session.AddSubpage(pageId).Id); surface.BeginEditTitle(); } return;
            case "collapse-page": case "expand-page":
                if (pageId is not null) session.SetPageCollapsed(pageId, command == "collapse-page"); return;
            case "page-up": case "page-down":
                if (pageId is not null) session.MovePageSibling(pageId, command == "page-up" ? -1 : 1); return;
            case "subpage":
                if (pageId is not null && !session.IndentPage(pageId)) saveStatus = "Cannot indent: choose a following sibling within the two-level limit"; return;
            case "promote-page":
                if (pageId is not null) session.PromotePage(pageId); return;
            default: throw new InvalidOperationException("Unknown command: " + command);
        }
    }
    private async Task VersionsAsync(string? pageId)
    {
        if (pageId is null) return; surface.FlushPendingText();
        var page = session.FindPage(pageId)!;
        if (page.Versions.Count == 0) { await MessageAsync("Page versions", "No saved versions yet. Choose History → Save Version to create a named recovery point. Undo and redo are available independently."); return; }
        var chosen = await ChooseAsync("Restore page version", page.Versions.Select(v => v.Created.ToString("g") + " — " + v.Description).ToList());
        if (chosen is not null && await ConfirmAsync("Restore this version?", "Replace the current page content with the selected snapshot? The restoration can be undone.", "Restore")) session.RestoreVersion(pageId, chosen.Value);
    }
    private async Task RecycleBinAsync()
    {
        var entries = session.Document.Trash.OrderByDescending(x => x.Deleted).ToList();
        if (entries.Count == 0) { await MessageAsync("Notebook recycle bin", "The recycle bin is empty. Deleted pages and pages from deleted sections or notebooks appear here."); return; }
        var chosen = await ChooseAsync("Restore a deleted page", entries.Select(d => d.Page.Title + " — " + d.Deleted.ToString("g")).ToList());
        if (chosen is not null)
        {
            if (!session.Document.Notebooks.Any(n => n.Sections.Count > 0)) session.AddNotebook("Recovered notes");
            session.RestorePage(entries[chosen.Value].Page.Id); Navigate(entries[chosen.Value].Page.Id);
        }
    }
    private async Task FindTagsAsync()
    {
        var matches = session.Pages.SelectMany(x => x.Page.Blocks.Where(b => b.Tags.Count > 0 || b.Kind == BlockKind.Checklist).Select(b => (Page: x.Page, Block: b))).ToList();
        if (matches.Count == 0) { await MessageAsync("Find tags", "No tags or to-do items yet. Select a note and choose To Do Tag, Important, or Question on Home."); return; }
        var chosen = await ChooseAsync("Tagged notes and to-do items", matches.Select(x => $"{x.Page.Title} · {(x.Block.Kind == BlockKind.Checklist ? x.Block.Checked ? "☑ " : "☐ " : "★ ")}{x.Block.Text[..Math.Min(70, x.Block.Text.Length)]}").ToList());
        if (chosen is not null) Navigate(matches[chosen.Value].Page.Id, matches[chosen.Value].Block.Id);
    }
    private async Task ReplaceTextAsync()
    {
        surface.EndEditing(); var find = await PromptAsync("Find text in all notebooks", ""); if (string.IsNullOrEmpty(find)) return;
        var replace = await PromptAsync("Replace with", ""); if (replace is null) return;
        if (!await ConfirmAsync("Replace in all notebooks?", $"Replace every case-insensitive occurrence of “{find}” in note text and table cells? Page titles are unchanged. This is one undoable action.", "Replace all")) return;
        var count = 0;
        session.Execute("Replace all", _ => {
            foreach (var (_, _, page) in session.Pages) foreach (var b in page.Blocks)
            {
                var replaced = RichText.ReplaceAll(b, find, replace);
                if (replaced > 0) { b.Height = Math.Max(b.Height, surface.Renderer.MeasureHeight(b)); page.Modified = DateTimeOffset.Now; count++; }
                foreach (var row in b.Cells) for (var i = 0; i < row.Count; i++) { var value = row[i].Replace(find, replace, StringComparison.OrdinalIgnoreCase); if (value != row[i]) { row[i] = value; page.Modified = DateTimeOffset.Now; count++; } }
            }
        });
        await MessageAsync("Replace complete", $"Updated {count} text containers or table cells.");
    }
    private async Task TemplateAsync()
    {
        var selected = await ChooseAsync("Create a page from a template", ["Blank page", "Meeting notes", "Project checklist", "Ideas and sketches"]);
        if (selected is null) return;
        var section = CurrentSection ?? session.AddNotebook("My notebook").Sections[0];
        var page = session.AddPage(section.Id, new[] { "Untitled page", "Meeting notes", "Project checklist", "Ideas and sketches" }[selected.Value]);
        session.EditPage(page.Id, "Apply page template", p => {
            if (selected == 1)
            {
                p.Blocks.Add(SampleWorkspace.Text("Meeting objective", 48, 145, 640, 48, 24, true, 0xFF7030A0));
                p.Blocks.Add(SampleWorkspace.Text("Attendees:\nDate: " + DateTime.Now.ToString("d") + "\nLocation:", 48, 210, 640, 115));
                p.Blocks.Add(SampleWorkspace.Text("Discussion notes", 48, 360, 640, 48, 22, true));
                p.Blocks.Add(new NoteBlock { Kind = BlockKind.Table, X = 48, Y = 490, Width = 720, Height = 168, Cells = [["Action item", "Owner", "Due date"], ["", "", ""], ["", "", ""], ["", "", ""]] });
            }
            else if (selected == 2)
            {
                p.Blocks.Add(SampleWorkspace.Text("From idea to done", 48, 145, 640, 48, 24, true, 0xFF7030A0));
                var tasks = new[] { "Define the goal", "Gather research", "Build a first version", "Review and refine", "Share the result" };
                for (var i = 0; i < tasks.Length; i++) p.Blocks.Add(new NoteBlock { Kind = BlockKind.Checklist, Text = tasks[i], X = 48, Y = 225 + 56 * i, Width = 640, Height = 46 });
            }
            else if (selected == 3) { p.Paper = PaperStyle.Dots; p.Blocks.Add(SampleWorkspace.Text("What could we make possible?", 48, 145, 680, 60, 26, true, 0xFF7030A0)); }
        });
        Navigate(page.Id);
    }
}
