using System.Globalization;
using System.Text;
using NoteSpace.Core;

namespace NoteSpace.Editor;

/// <summary>Build a sorted view without changing a page's persisted order, depth or collapse state.</summary>
public static class PagePresentation
{
    public static IReadOnlyList<PageOutlineEntry> Build(IReadOnlyList<NotePage> pages, PageSortMode sort = PageSortMode.Manual)
    {
        ArgumentNullException.ThrowIfNull(pages);
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        if (sort == PageSortMode.Manual) return PageOutline.Build(pages);
        var outline = PageOutline.Build(pages);
        var siblings = new Dictionary<int, List<int>>();
        foreach (var entry in outline)
        {
            if (!siblings.TryGetValue(entry.ParentIndex, out var children)) siblings.Add(entry.ParentIndex, children = []);
            children.Add(entry.Index);
        }
        int Compare(int a, int b)
        {
            var x = pages[a]; var y = pages[b];
            var result = sort switch {
                PageSortMode.TitleAscending => StringComparer.CurrentCultureIgnoreCase.Compare(x.Title, y.Title),
                PageSortMode.TitleDescending => StringComparer.CurrentCultureIgnoreCase.Compare(y.Title, x.Title),
                PageSortMode.ModifiedNewest => y.Modified.CompareTo(x.Modified),
                PageSortMode.CreatedNewest => y.Created.CompareTo(x.Created),
                _ => 0
            };
            return result == 0 ? a.CompareTo(b) : result; // Stable ties preserve manual sibling order.
        }
        foreach (var children in siblings.Values) children.Sort(Compare);
        var reordered = new List<PageOutlineEntry>(pages.Count);
        void Append(int parent, int outputParent)
        {
            if (!siblings.TryGetValue(parent, out var children)) return;
            foreach (var original in children)
            {
                var entry = outline[original]; var index = reordered.Count;
                reordered.Add(entry with { Index = index, ParentIndex = outputParent });
                Append(original, index); // Normalized outline depth is bounded to three levels.
                reordered[index] = reordered[index] with { EndIndex = reordered.Count };
            }
        }
        Append(-1, -1); return reordered;
    }

    /// <summary>Bounded plain-text preview. Never decodes attachments, lays out text or scans a whole notebook.</summary>
    public static string Preview(NotePage page, int maximumLength = 110)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (maximumLength is < 8 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumLength));
        var scanned = 0;
        string Shorten(string text)
        {
            // Limit work even for very long leading whitespace or pathological text elements.
            var budget = Math.Min(4096 - scanned, text.Length);
            if (budget <= 0) return "";
            scanned += budget;
            var sample = text[..budget]; var result = new StringBuilder(maximumLength + 1);
            var elements = StringInfo.GetTextElementEnumerator(sample); var space = false;
            while (elements.MoveNext())
            {
                var item = elements.GetTextElement();
                if (char.IsWhiteSpace(item, 0)) { space = result.Length > 0; continue; }
                // Do not copy a possibly truncated final grapheme at the scan-budget edge.
                if (budget < text.Length && elements.ElementIndex + item.Length == sample.Length) break;
                if (result.Length + item.Length + (space ? 1 : 0) > maximumLength - 1)
                { if (result.Length > 0) result.Append('…'); return result.ToString(); }
                if (space) result.Append(' ');
                result.Append(item); space = false;
            }
            if (budget < text.Length && result.Length > 0) result.Append('…');
            return result.ToString();
        }
        foreach (var block in page.Blocks.Take(32))
        {
            var text = block.Kind is BlockKind.Image or BlockKind.Attachment ? block.FileName : block.Text;
            var preview = Shorten(text);
            if (preview.Length > 0) return preview;
            if (block.Kind == BlockKind.Table)
                foreach (var cell in block.Cells.Take(4).SelectMany(row => row.Take(8)))
                { preview = Shorten(cell); if (preview.Length > 0) return preview; if (scanned >= 4096) break; }
            if (scanned >= 4096) break;
        }
        return page.Ink.Count > 0 ? $"Drawing · {page.Ink.Count} strokes" : page.Blocks.Count > 0 ? "Page content" : "Empty page";
    }
}

public readonly record struct PageViewport(float OffsetX = 0, float OffsetY = 0, float Zoom = 1)
{
    public PageViewport() : this(0, 0, 1) { }
    public void Validate()
    {
        if (!float.IsFinite(OffsetX) || !float.IsFinite(OffsetY) || !float.IsFinite(Zoom)
            || OffsetX is < 0 or > 200000 || OffsetY is < 0 or > 200000 || Zoom is < 0.25f or > 2.5f)
            throw new ArgumentOutOfRangeException(nameof(PageViewport));
    }
}
public sealed record PageVisit(string PageId, PageViewport Viewport);

/// <summary>Bounded navigation history separate from document undo. IDs and viewports only;
/// stale pages are skipped, not restored. Single-writer, with no workspace or UI ownership.</summary>
public sealed class PageNavigationHistory
{
    private readonly List<PageVisit> visits = [];
    private int position = -1;
    public int Capacity { get; }
    public int Count => visits.Count;
    public PageVisit? Current => position < 0 ? null : visits[position];
    public PageNavigationHistory(int capacity = 100)
    {
        if (capacity is < 2 or > 1000) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }
    public void Clear() { visits.Clear(); position = -1; }
    public void Record(PageVisit visit)
    {
        Validate(visit);
        if (Current?.PageId == visit.PageId) { visits[position] = visit; return; }
        if (position + 1 < visits.Count) visits.RemoveRange(position + 1, visits.Count - position - 1);
        visits.Add(visit);
        if (visits.Count > Capacity) visits.RemoveAt(0);
        position = visits.Count - 1;
    }
    public void UpdateCurrent(PageVisit visit)
    {
        Validate(visit);
        if (Current?.PageId == visit.PageId) visits[position] = visit;
    }
    public PageVisit? Peek(int direction, Func<string, bool> exists)
    {
        var index = Find(direction, exists); return index < 0 ? null : visits[index];
    }
    public PageVisit? Move(int direction, Func<string, bool> exists)
    {
        var index = Find(direction, exists);
        if (index < 0) return null;
        position = index; return visits[index];
    }
    private int Find(int direction, Func<string, bool> exists)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        ArgumentNullException.ThrowIfNull(exists);
        for (var at = position + direction; at >= 0 && at < visits.Count; at += direction)
            if (exists(visits[at].PageId)) return at;
        return -1;
    }
    private static void Validate(PageVisit visit)
    {
        ArgumentNullException.ThrowIfNull(visit); ArgumentException.ThrowIfNullOrWhiteSpace(visit.PageId);
        visit.Viewport.Validate();
    }
}

public readonly record struct NavigationPaneWidths(double Notebooks, double Pages, double Search);
public static class NavigationPaneLayout
{
    /// <summary>Fit desktop side panes while reserving at least 280 logical pixels for the page.
    /// Requested widths remain unchanged; narrow/phone overlay policy belongs to the host.</summary>
    public static NavigationPaneWidths Fit(double available, NavigationPreferences preferences, bool notebooks, bool pages, bool search)
    {
        if (!double.IsFinite(available) || available < 0) throw new ArgumentOutOfRangeException(nameof(available));
        ArgumentNullException.ThrowIfNull(preferences); preferences.Validate();
        var n = notebooks ? preferences.NotebookWidth : 0; var p = pages ? preferences.PageWidth : 0; var s = search ? preferences.SearchWidth : 0;
        var total = n + p + s; var budget = Math.Max(0, available - 280);
        if (total <= budget) return new(n, p, s);
        var minN = notebooks ? 140d : 0; var minP = pages ? 140d : 0; var minS = search ? 200d : 0;
        var minimum = minN + minP + minS;
        if (minimum >= budget)
        {
            var scale = minimum == 0 ? 0 : budget / minimum;
            return new(minN * scale, minP * scale, minS * scale);
        }
        var fraction = (budget - minimum) / (total - minimum);
        return new(minN + (n - minN) * fraction, minP + (p - minP) * fraction, minS + (s - minS) * fraction);
    }
}
