using NoteSpace.Core;

namespace NoteSpace.Editor;

/// <summary>Immutable geometry snapshot. Rebuild after content changes; queries reuse
/// caller-owned buffers and return indices in document paint order. No UI/Skia dependency.</summary>
public sealed class PageContentIndex
{
    private readonly Dictionary<string, int> blockIds = new(StringComparer.Ordinal);
    private readonly BoundsTree blocks;
    private readonly BoundsTree ink;
    public NotePage Page { get; }
    public NoteRect Extent { get; }
    public long InkPointCount { get; }
    public int LastCandidatesTested { get; private set; }

    public PageContentIndex(NotePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        var blockBounds = new NoteRect[page.Blocks.Count];
        var inkBounds = new NoteRect[page.Ink.Count];
        var right = 1100f; var bottom = 760f;
        for (var i = 0; i < blockBounds.Length; i++)
        {
            var b = page.Blocks[i]; blockIds.Add(b.Id, i);
            // Include the top move grip, selection handle, and tag badges in culling.
            blockBounds[i] = new(b.X - 4, b.Y - 16, b.Width + 12 + b.Tags.Count * 44, b.Height + 24);
            right = Math.Max(right, b.X + b.Width + 80); bottom = Math.Max(bottom, b.Y + b.Height + 120);
        }
        long points = 0;
        for (var i = 0; i < inkBounds.Length; i++)
        {
            var stroke = page.Ink[i]; var b = inkBounds[i] = InkGeometry.Bounds(stroke);
            points += stroke.Points.Count;
            right = Math.Max(right, b.X + b.Width + 80); bottom = Math.Max(bottom, b.Y + b.Height + 120);
        }
        blocks = new(blockBounds); ink = new(inkBounds);
        Extent = new(0, 0, right, bottom); InkPointCount = points;
    }

    public int BlockIndex(string id) => blockIds.GetValueOrDefault(id, -1);

    public void QueryBlocks(NoteRect area, List<int> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        LastCandidatesTested = blocks.Query(area, result);
    }
    public void QueryInk(NoteRect area, List<int> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        LastCandidatesTested = ink.Query(area, result);
    }

    private sealed class BoundsTree
    {
        private readonly record struct Item(int Index, NoteRect Bounds);
        private readonly record struct Node(NoteRect Bounds, int Start, int Count, int Left, int Right);
        private readonly Item[] items;
        private readonly List<Node> nodes = [];
        private sealed class AxisComparer(bool vertical) : IComparer<Item>
        {
            public int Compare(Item a, Item b) => (vertical ? a.Bounds.Y + a.Bounds.Height / 2 : a.Bounds.X + a.Bounds.Width / 2)
                .CompareTo(vertical ? b.Bounds.Y + b.Bounds.Height / 2 : b.Bounds.X + b.Bounds.Width / 2);
        }
        private static readonly AxisComparer Horizontal = new(false), Vertical = new(true);
        public BoundsTree(NoteRect[] bounds)
        {
            items = new Item[bounds.Length];
            for (var i = 0; i < bounds.Length; i++) items[i] = new(i, bounds[i]);
            if (items.Length > 0) Build(0, items.Length);
        }
        private int Build(int start, int count)
        {
            var x = float.MaxValue; var y = float.MaxValue; var right = float.MinValue; var bottom = float.MinValue;
            for (var i = start; i < start + count; i++)
            {
                var b = items[i].Bounds; x = Math.Min(x, b.X); y = Math.Min(y, b.Y);
                right = Math.Max(right, b.X + b.Width); bottom = Math.Max(bottom, b.Y + b.Height);
            }
            var bounds = new NoteRect(x, y, right - x, bottom - y);
            var node = nodes.Count; nodes.Add(default);
            if (count <= 8) nodes[node] = new(bounds, start, count, -1, -1);
            else
            {
                Array.Sort(items, start, count, bounds.Height > bounds.Width ? Vertical : Horizontal);
                var half = count / 2; var left = Build(start, half); var rightNode = Build(start + half, count - half);
                nodes[node] = new(bounds, 0, 0, left, rightNode);
            }
            return node;
        }
        public int Query(NoteRect area, List<int> result)
        {
            result.Clear(); var tested = 0;
            if (!float.IsFinite(area.X) || !float.IsFinite(area.Y) || !float.IsFinite(area.Width) || !float.IsFinite(area.Height)
                || area.Width < 0 || area.Height < 0) throw new ArgumentOutOfRangeException(nameof(area));
            if (nodes.Count > 0) Visit(0, area, result, ref tested);
            result.Sort(); return tested;
        }
        private void Visit(int index, NoteRect area, List<int> result, ref int tested)
        {
            var node = nodes[index]; if (!node.Bounds.Intersects(area)) return;
            if (node.Count == 0) { Visit(node.Left, area, result, ref tested); Visit(node.Right, area, result, ref tested); return; }
            for (var i = node.Start; i < node.Start + node.Count; i++)
            {
                tested++; if (items[i].Bounds.Intersects(area)) result.Add(items[i].Index);
            }
        }
    }
}
