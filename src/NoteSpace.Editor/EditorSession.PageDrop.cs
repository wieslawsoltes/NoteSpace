using NoteSpace.Core;

namespace NoteSpace.Editor;

public enum PageDropPlacement { Before, After, Inside }
public sealed record PageDropRequest(string PageId, string TargetPageId, PageDropPlacement Placement);

public sealed partial class EditorSession
{
    private sealed record PageMovePlan(NoteSection Source, NoteSection Target, PageOutlineEntry Entry, int DestinationIndex, int Level, NotePage TargetPage);

    private PageMovePlan? PlanPageMove(PageDropRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Placement)) return null;
        if (FindPage(request.PageId) is null || FindPage(request.TargetPageId) is null) return null;
        var (source, outline, entry) = OutlineFor(request.PageId);
        var (target, _, destination) = OutlineFor(request.TargetPageId);
        if (source == target && destination.Index >= entry.Index && destination.Index < entry.EndIndex) return null;
        var level = destination.Level + (request.Placement == PageDropPlacement.Inside ? 1 : 0);
        var height = outline.Skip(entry.Index).Take(entry.EndIndex - entry.Index).Max(x => x.Level - entry.Level);
        if (level + height > PageOutline.MaximumLevel) return null;
        var at = request.Placement == PageDropPlacement.Before ? destination.Index : destination.EndIndex;
        if (source == target && at >= entry.EndIndex) at -= entry.EndIndex - entry.Index;
        return new(source, target, entry, at, level, destination.Page);
    }

    /// <summary>Read-only preview validation. A drop is revalidated when committed.</summary>
    public bool CanMovePageRelative(PageDropRequest request) => PlanPageMove(request) is not null;

    /// <summary>Move the complete subtree before/after a sibling or as the target's last
    /// child. Returns false for invalid drops; invalid/no-op drops do not consume undo.</summary>
    public bool MovePageRelative(PageDropRequest request)
    {
        var plan = PlanPageMove(request); if (plan is null) return false;
        if (plan.Source == plan.Target && plan.DestinationIndex == plan.Entry.Index && plan.Level == plan.Entry.Level) return false;
        Execute("Move page outline", w => {
            PageOutline.Normalize(plan.Source.Pages);
            if (plan.Target != plan.Source) PageOutline.Normalize(plan.Target.Pages);
            var group = plan.Source.Pages.GetRange(plan.Entry.Index, plan.Entry.EndIndex - plan.Entry.Index);
            plan.Source.Pages.RemoveRange(plan.Entry.Index, group.Count);
            foreach (var page in group) page.Level += plan.Level - plan.Entry.Level;
            plan.Target.Pages.InsertRange(plan.DestinationIndex, group);
            if (request.Placement == PageDropPlacement.Inside) plan.TargetPage.IsCollapsed = false;
            w.Settings.SelectedPageId = request.PageId;
        }, true);
        return true;
    }
}
