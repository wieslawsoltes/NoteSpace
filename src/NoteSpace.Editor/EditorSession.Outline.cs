using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed partial class EditorSession
{
    private (NoteSection Section, IReadOnlyList<PageOutlineEntry> Outline, PageOutlineEntry Entry) OutlineFor(string pageId)
    {
        var location = Pages.FirstOrDefault(x => x.Page.Id == pageId);
        if (location.Page is null) throw new ArgumentException("Page not found.", nameof(pageId));
        var outline = PageOutline.Build(location.Section.Pages);
        return (location.Section, outline, outline.First(x => x.Page.Id == pageId));
    }

    public NotePage AddSubpage(string parentId, string title = "Untitled page")
    {
        var (section, _, parent) = OutlineFor(parentId);
        if (parent.Level == PageOutline.MaximumLevel) throw new InvalidOperationException("Pages support two levels of subpages.");
        var page = new NotePage { Title = CleanTitle(title), Level = parent.Level + 1 };
        Execute("New subpage", w => {
            PageOutline.Normalize(section.Pages); parent.Page.IsCollapsed = false;
            section.Pages.Insert(parent.EndIndex, page); w.Settings.SelectedPageId = page.Id;
        }, true);
        return page;
    }

    public void SetPageCollapsed(string pageId, bool collapsed)
    {
        var (_, outlineBefore, entry) = OutlineFor(pageId);
        var selectedDescendant = outlineBefore.Skip(entry.Index + 1).Take(entry.EndIndex - entry.Index - 1)
            .Any(x => x.Page.Id == Document.Settings.SelectedPageId);
        // Search can reveal a child without clearing the stored collapse flag.
        // Collapsing that temporarily revealed group must still select its parent.
        if (!entry.HasChildren || entry.Page.IsCollapsed == collapsed && !(collapsed && selectedDescendant)) return;
        Execute(collapsed ? "Collapse subpages" : "Expand subpages", w => {
            var (_, outline, current) = OutlineFor(pageId);
            current.Page.IsCollapsed = collapsed;
            if (collapsed && outline.Skip(current.Index + 1).Take(current.EndIndex - current.Index - 1)
                .Any(x => x.Page.Id == w.Settings.SelectedPageId)) w.Settings.SelectedPageId = pageId;
        }, true);
    }

    public bool IndentPage(string pageId)
    {
        var (section, outline, entry) = OutlineFor(pageId);
        var previous = entry.Index - 1;
        while (previous >= 0 && outline[previous].Level > entry.Level) previous--;
        if (previous < 0 || outline[previous].Level != entry.Level || outline[previous].ParentIndex != entry.ParentIndex
            || outline.Skip(entry.Index).Take(entry.EndIndex - entry.Index).Any(x => x.Level >= PageOutline.MaximumLevel)) return false;
        Execute("Make subpage", _ => {
            PageOutline.Normalize(section.Pages);
            for (var i = entry.Index; i < entry.EndIndex; i++) section.Pages[i].Level++;
            outline[previous].Page.IsCollapsed = false;
        }, true);
        return true;
    }

    public bool PromotePage(string pageId)
    {
        var (section, outline, entry) = OutlineFor(pageId);
        if (entry.ParentIndex < 0) return false;
        var count = entry.EndIndex - entry.Index;
        var destination = outline[entry.ParentIndex].EndIndex - count;
        Execute("Promote page", _ => {
            PageOutline.Normalize(section.Pages);
            var group = section.Pages.GetRange(entry.Index, count);
            section.Pages.RemoveRange(entry.Index, count);
            foreach (var page in group) page.Level--;
            // Insert after the old parent's subtree so following siblings do not
            // accidentally become children of the promoted page.
            section.Pages.InsertRange(destination, group);
        }, true);
        return true;
    }

    public bool MovePageSibling(string pageId, int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var (section, outline, entry) = OutlineFor(pageId);
        var sibling = direction < 0 ? entry.Index - 1 : entry.EndIndex;
        if (direction < 0) while (sibling >= 0 && outline[sibling].Level > entry.Level) sibling--;
        if (sibling < 0 || sibling >= outline.Count || outline[sibling].Level != entry.Level
            || outline[sibling].ParentIndex != entry.ParentIndex) return false;
        var count = entry.EndIndex - entry.Index;
        var destination = direction < 0 ? sibling : outline[sibling].EndIndex - count;
        Execute("Reorder page group", _ => {
            PageOutline.Normalize(section.Pages);
            var group = section.Pages.GetRange(entry.Index, count);
            section.Pages.RemoveRange(entry.Index, count); section.Pages.InsertRange(destination, group);
        }, true);
        return true;
    }

    /// <summary>Move a page and all subpages as a root group. Index is in the destination
    /// after removal; an index inside another group is advanced past that group.</summary>
    private void MovePageGroup(string pageId, string sectionId, int index)
    {
        var (source, _, entry) = OutlineFor(pageId);
        var target = FindSection(sectionId) ?? throw new ArgumentException("Section not found.", nameof(sectionId));
        Execute("Move page group", _ => {
            PageOutline.Normalize(source.Pages); if (target != source) PageOutline.Normalize(target.Pages);
            var count = entry.EndIndex - entry.Index;
            var group = source.Pages.GetRange(entry.Index, count); source.Pages.RemoveRange(entry.Index, count);
            foreach (var page in group) page.Level -= entry.Level;
            var at = Math.Clamp(index, 0, target.Pages.Count);
            foreach (var root in PageOutline.Build(target.Pages).Where(x => x.Level == 0))
                if (at > root.Index && at < root.EndIndex) { at = root.EndIndex; break; }
            target.Pages.InsertRange(at, group);
        }, true);
    }
}
