using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed partial class EditorSession
{
    public SectionGroup? FindSectionGroup(string? id) => Document.Notebooks.SelectMany(n => n.SectionGroups).FirstOrDefault(g => g.Id == id);
    private Notebook RequireNotebook(string id) => Document.Notebooks.FirstOrDefault(n => n.Id == id) ?? throw new ArgumentException("Notebook not found.", nameof(id));
    private (Notebook Notebook, SectionGroup Group) RequireGroup(string id)
    {
        foreach (var notebook in Document.Notebooks)
            if (notebook.SectionGroups.FirstOrDefault(g => g.Id == id) is { } group) return (notebook, group);
        throw new ArgumentException("Section group not found.", nameof(id));
    }
    private static void RequireParent(Notebook notebook, string? id)
    {
        if (id is not null && !notebook.SectionGroups.Any(g => g.Id == id)) throw new ArgumentException("The parent group must belong to the destination notebook.", nameof(id));
    }

    public SectionGroup AddSectionGroup(string notebookId, string title, string? parentId = null)
    {
        var notebook = RequireNotebook(notebookId); RequireParent(notebook, parentId);
        var group = new SectionGroup { Title = CleanTitle(title), ParentId = parentId };
        Execute("New section group", _ => { notebook.SectionGroups.Add(group); ExpandPath(notebook, parentId); }, true);
        return group;
    }

    public NoteSection AddSectionInGroup(string notebookId, string title, string? groupId = null)
    {
        var notebook = RequireNotebook(notebookId); RequireParent(notebook, groupId);
        var section = new NoteSection { Title = CleanTitle(title), GroupId = groupId, Pages = [new NotePage()] };
        Execute("New section", w => {
            notebook.Sections.Add(section); ExpandPath(notebook, groupId);
            w.Settings.SelectedPageId = section.Pages[0].Id;
        }, true);
        return section;
    }

    private static void ExpandPath(Notebook notebook, string? groupId)
    {
        var path = NotebookGroups.Ancestors(notebook, groupId).ToHashSet(StringComparer.Ordinal);
        foreach (var group in notebook.SectionGroups) if (path.Contains(group.Id)) group.IsCollapsed = false;
    }

    public void RenameSectionGroup(string groupId, string title) => Execute("Rename section group", _ => RequireGroup(groupId).Group.Title = CleanTitle(title), true);
    public void SetSectionGroupCollapsed(string groupId, bool collapsed) => Execute(collapsed ? "Collapse section group" : "Expand section group", _ => RequireGroup(groupId).Group.IsCollapsed = collapsed, true);

    /// <summary>Reparent a complete group subtree, including its sections, across notebooks.
    /// Validation rejects cycles, invalid destinations and over-depth moves atomically.</summary>
    public void MoveSectionGroup(string groupId, string notebookId, string? parentId = null)
    {
        var (source, group) = RequireGroup(groupId); var target = RequireNotebook(notebookId); RequireParent(target, parentId);
        var ids = NotebookGroups.SubtreeIds(source, groupId);
        if (parentId is not null && ids.Contains(parentId)) throw new InvalidOperationException("A section group cannot be moved into itself or its descendants.");
        if (source == target && group.ParentId == parentId) return;
        Execute("Move section group", _ => {
            if (source != target)
            {
                var groups = source.SectionGroups.Where(g => ids.Contains(g.Id)).ToList();
                var sections = source.Sections.Where(s => s.GroupId is { } id && ids.Contains(id)).ToList();
                source.SectionGroups.RemoveAll(g => ids.Contains(g.Id)); target.SectionGroups.AddRange(groups);
                source.Sections.RemoveAll(s => sections.Contains(s)); target.Sections.AddRange(sections);
            }
            group.ParentId = parentId; ExpandPath(target, parentId);
        }, true);
    }

    /// <summary>Remove only the group, keeping its sections and child groups in its parent.</summary>
    public void UngroupSectionGroup(string groupId)
    {
        var (notebook, group) = RequireGroup(groupId);
        Execute("Ungroup sections", _ => {
            foreach (var section in notebook.Sections.Where(s => s.GroupId == groupId)) section.GroupId = group.ParentId;
            foreach (var child in notebook.SectionGroups.Where(g => g.ParentId == groupId)) child.ParentId = group.ParentId;
            notebook.SectionGroups.Remove(group); ExpandPath(notebook, group.ParentId);
        }, true);
    }

    public void MoveSection(string sectionId, string notebookId, string? groupId = null)
    {
        var section = FindSection(sectionId) ?? throw new ArgumentException("Section not found.", nameof(sectionId));
        var source = Document.Notebooks.First(n => n.Sections.Contains(section)); var target = RequireNotebook(notebookId); RequireParent(target, groupId);
        if (source == target && section.GroupId == groupId) return;
        Execute("Move section", _ => {
            source.Sections.Remove(section); target.Sections.Add(section); section.GroupId = groupId; ExpandPath(target, groupId);
        }, true);
    }

    public bool MoveSectionSibling(string sectionId, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var section = FindSection(sectionId) ?? throw new ArgumentException("Section not found.", nameof(sectionId));
        var notebook = Document.Notebooks.First(n => n.Sections.Contains(section));
        var siblings = notebook.Sections.Where(s => s.GroupId == section.GroupId).ToList(); var index = siblings.IndexOf(section) + direction;
        if (index < 0 || index >= siblings.Count) return false;
        Execute("Reorder section", _ => {
            var at = notebook.Sections.IndexOf(siblings[index]); notebook.Sections.Remove(section); notebook.Sections.Insert(at, section);
        }, true); return true;
    }

    public bool MoveSectionGroupSibling(string groupId, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var (notebook, group) = RequireGroup(groupId);
        var siblings = notebook.SectionGroups.Where(g => g.ParentId == group.ParentId).ToList(); var index = siblings.IndexOf(group) + direction;
        if (index < 0 || index >= siblings.Count) return false;
        Execute("Reorder section group", _ => {
            var at = notebook.SectionGroups.IndexOf(siblings[index]); notebook.SectionGroups.Remove(group); notebook.SectionGroups.Insert(at, group);
        }, true); return true;
    }
}
