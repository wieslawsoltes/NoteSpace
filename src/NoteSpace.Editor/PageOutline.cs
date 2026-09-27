using NoteSpace.Core;

namespace NoteSpace.Editor;

/// <summary>A page and its half-open subtree range in section order.</summary>
public sealed record PageOutlineEntry(NotePage Page, int Index, int Level, int ParentIndex, int EndIndex)
{
    public bool HasChildren => EndIndex > Index + 1;
}

/// <summary>Linear-time, UI-independent navigation over the persisted flat page outline.</summary>
public static class PageOutline
{
    public const int MaximumLevel = 2;

    // Older documents allowed orphan indentation. Interpret it defensively without
    // modifying a document during a query; edits normalize the affected section.
    public static IReadOnlyList<PageOutlineEntry> Build(IReadOnlyList<NotePage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
        var result = new PageOutlineEntry[pages.Count];
        var stack = new Stack<int>(MaximumLevel + 1);
        var previousLevel = -1;
        for (var i = 0; i < pages.Count; i++)
        {
            var level = Math.Clamp(pages[i].Level, 0, Math.Min(MaximumLevel, previousLevel + 1));
            while (stack.Count > 0 && result[stack.Peek()].Level >= level)
            {
                var ending = stack.Pop(); result[ending] = result[ending] with { EndIndex = i };
            }
            result[i] = new(pages[i], i, level, stack.Count > 0 ? stack.Peek() : -1, pages.Count);
            stack.Push(i); previousLevel = level;
        }
        return result;
    }

    public static IReadOnlyList<PageOutlineEntry> Visible(IReadOnlyList<PageOutlineEntry> outline, string? revealPageId = null)
    {
        var revealIndex = -1;
        for (var i = 0; i < outline.Count; i++) if (outline[i].Page.Id == revealPageId) { revealIndex = i; break; }
        var visible = new List<PageOutlineEntry>(outline.Count);
        for (var i = 0; i < outline.Count;)
        {
            var item = outline[i]; visible.Add(item);
            var revealsDescendant = revealIndex > i && revealIndex < item.EndIndex;
            i = item.HasChildren && item.Page.IsCollapsed && !revealsDescendant ? item.EndIndex : i + 1;
        }
        return visible;
    }

    public static void Normalize(IReadOnlyList<NotePage> pages)
    {
        foreach (var item in Build(pages)) item.Page.Level = item.Level;
    }
}
