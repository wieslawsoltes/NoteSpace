using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private async Task ManageGroupsAsync(string command, string? id)
    {
        var group = session.FindSectionGroup(id);
        var notebook = session.Document.Notebooks.FirstOrDefault(n => n.Id == id || n.SectionGroups.Any(g => g.Id == id))
            ?? session.Document.Notebooks.FirstOrDefault(n => n.Sections.Any(s => s.Id == CurrentSection?.Id));
        switch (command)
        {
            case "new-section-group":
                if (notebook is null) throw new InvalidOperationException("Create a notebook first.");
                var name = await PromptAsync("New section group", "New section group");
                if (name is not null)
                {
                    var added = session.AddSectionGroup(notebook.Id, name, group?.Id);
                    navigationOpen = true; focusMode = false; ApplyLayout();
                    notebooks.RevealGroup(notebook.Id, added.Id);
                }
                break;
            case "rename-group":
                if (group is null) return;
                var title = await PromptAsync("Rename section group", group.Title);
                if (title is not null) session.RenameSectionGroup(group.Id, title);
                break;
            case "collapse-group": case "expand-group":
                if (group is not null) session.SetSectionGroupCollapsed(group.Id, command == "collapse-group");
                break;
            case "group-up": case "group-down":
                if (group is not null) session.MoveSectionGroupSibling(group.Id, command == "group-up" ? -1 : 1);
                break;
            case "ungroup":
                if (group is not null && await ConfirmAsync("Ungroup sections?", "Remove this group and move its sections and child groups into its parent? All pages are kept. This action can be undone.", "Ungroup"))
                    session.UngroupSectionGroup(group.Id);
                break;
            case "move-section": case "move-group":
                var section = session.FindSection(id);
                if (command == "move-group" && group is null || command == "move-section" && section is null) return;
                var excluded = group is not null && notebook is not null ? NotebookGroups.SubtreeIds(notebook, group.Id) : [];
                var destinations = new List<(string NotebookId, string? GroupId, string Label)>();
                foreach (var n in session.Document.Notebooks)
                {
                    destinations.Add((n.Id, null, n.Title + " / (notebook root)"));
                    foreach (var item in NotebookGroups.Build(n, includeCollapsed: true).Where(x => x.Group is not null && !excluded.Contains(x.Id)))
                        destinations.Add((n.Id, item.Id, NotebookGroups.Path(n, item.Id)));
                }
                var chosen = await ChooseAsync(command == "move-section" ? "Move section" : "Move section group", destinations.Select(x => x.Label).ToArray());
                if (chosen is not null)
                {
                    var destination = destinations[chosen.Value];
                    if (command == "move-section") session.MoveSection(section!.Id, destination.NotebookId, destination.GroupId);
                    else session.MoveSectionGroup(group!.Id, destination.NotebookId, destination.GroupId);
                    notebooks.RevealSelection();
                }
                break;
            default: throw new InvalidOperationException("Unknown section group command.");
        }
    }

    private void DropPage(PageDropRequest request)
    {
        surface.EndEditing();
        if (surface.HasPendingText) { saveStatus = "Resolve the current text edit before moving pages"; UpdateStatus(); return; }
        try { if (session.MovePageRelative(request)) Navigate(request.PageId); }
        catch (Exception error) { saveStatus = error.Message; UpdateStatus(); }
        Report();
    }
}
