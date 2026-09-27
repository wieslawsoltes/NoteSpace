using System.Globalization;
using System.Text.RegularExpressions;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.Rendering.Skia;

public sealed partial class PageRenderer
{
    private readonly record struct StyleKey(string Family, float Size, bool Bold, bool Italic, bool Underline, bool Strike,
        uint Color, uint Highlight, int Alignment, bool Bullets, bool Numbered)
    {
        public static StyleKey From(TextFormat f) => new(f.FontFamily, f.FontSize, f.Bold, f.Italic, f.Underline, f.Strike, f.Color, f.Highlight, f.Alignment, f.Bullets, f.Numbered);
    }
    private sealed class LayoutStamp(NoteBlock block)
    {
        private readonly string text = block.Text;
        private readonly float width = block.Width;
        private readonly BlockKind kind = block.Kind;
        private readonly StyleKey style = StyleKey.From(block.Format);
        private readonly (int Start, int Length, StyleKey Style)[] marks = block.Marks.Select(m => (m.Start, m.Length, StyleKey.From(m.Format))).ToArray();
        public bool Matches(NoteBlock b)
        {
            // Strings are immutable. Reference checks avoid re-hashing megabytes on every pan.
            if (!ReferenceEquals(text, b.Text) || width != b.Width || kind != b.Kind || style != StyleKey.From(b.Format) || marks.Length != b.Marks.Count) return false;
            for (var i = 0; i < marks.Length; i++)
            {
                var m = b.Marks[i]; if (marks[i] != (m.Start, m.Length, StyleKey.From(m.Format))) return false;
            }
            return true;
        }
    }
    private sealed record CachedLayout(LayoutStamp Stamp, TextLayout Value, NoteBlock Block, long? Revision, long Used, long Weight);
    private readonly Dictionary<string, CachedLayout> layouts = new();
    private long layoutWeight;
    private const long MaximumLayoutWeight = 8 * 1024 * 1024;
    private static readonly Regex Tokens = new(@"\r\n|\r|\n|[^\S\r\n]+|[^\s]+", RegexOptions.CultureInvariant);

    public TextLayout Layout(NoteBlock block) => LayoutCore(block, null);
    private TextLayout LayoutCore(NoteBlock b, long? revision)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(b);
        if (layouts.TryGetValue(b.Id, out var cached)
            && (revision.HasValue && cached.Revision == revision && ReferenceEquals(cached.Block, b) || cached.Stamp.Matches(b)))
        {
            layouts[b.Id] = cached with { Used = ++useClock, Revision = revision, Block = b };
            Statistics.LayoutHits++; return cached.Value;
        }
        Statistics.LayoutBuilds++;
        var fragments = new List<TextFragment>(); var lines = new List<TextLine>();
        var baseFormat = DocumentJson.CloneFormat(b.Format);
        var indent = b.Kind == BlockKind.Checklist || baseFormat.Bullets || baseFormat.Numbered ? 24f : 0f;
        var width = Math.Max(12, b.Width - 24 - indent);
        var x = 0f; var y = 0f; var lineHeight = baseFormat.FontSize * 1.45f;
        var line = new List<(string Text, float X, float Width, TextFormat Format)>(); var lineNumber = 1;
        void Flush()
        {
            var first = fragments.Count;
            var shift = baseFormat.Alignment == 1 ? Math.Max(0, (width - x) / 2) : baseFormat.Alignment == 2 ? Math.Max(0, width - x) : 0;
            if (baseFormat.Bullets || baseFormat.Numbered)
                fragments.Add(new(baseFormat.Numbered ? (lineNumber++).ToString() + "." : "•", 12, y + lineHeight - 5, 20, baseFormat));
            foreach (var f in line) fragments.Add(new(f.Text, 12 + indent + f.X + shift, y + lineHeight - 5, f.Width, f.Format));
            lines.Add(new(first, fragments.Count - first, y - 4, y + lineHeight + 6));
            y += lineHeight; x = 0; line.Clear(); lineHeight = baseFormat.FontSize * 1.45f;
        }
        void Piece(string text, TextFormat format)
        {
            var font = Font(format); var measured = font.MeasureText(text);
            if (x > 0 && x + measured > width && !string.IsNullOrWhiteSpace(text)) Flush();
            if (measured <= width)
            {
                line.Add((text, x, measured, format)); x += measured; lineHeight = Math.Max(lineHeight, format.FontSize * 1.45f); return;
            }
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var value = elements.GetTextElement(); var advance = font.MeasureText(value);
                if (x > 0 && x + advance > width) Flush();
                line.Add((value, x, advance, format)); x += advance; lineHeight = Math.Max(lineHeight, format.FontSize * 1.45f);
            }
        }
        // Resolve legacy overlaps once. Advance monotonically through canonical style
        // runs rather than scanning every mark and sorting boundaries for every token.
        var runs = RichText.GetRuns(b); var runIndex = 0;
        foreach (Match token in Tokens.Matches(b.Text))
        {
            if (token.Value is "\n" or "\r" or "\r\n") { Flush(); continue; }
            var at = token.Index; var end = at + token.Length;
            while (at < end)
            {
                while (runIndex < runs.Count && runs[runIndex].End <= at) runIndex++;
                var run = runs[runIndex]; var to = Math.Min(end, run.End);
                Piece(b.Text[at..to], run.Format); at = to;
            }
        }
        Flush();
        var result = new TextLayout(fragments, y + 24) { Lines = lines };
        var weight = b.Text.Length + (long)fragments.Count * 64 + (long)b.Marks.Count * 64;
        if (layouts.Remove(b.Id, out var old)) layoutWeight -= old.Weight;
        if (weight <= MaximumLayoutWeight)
        {
            while (layouts.Count > 0 && (layouts.Count >= 2000 || layoutWeight + weight > MaximumLayoutWeight))
            {
                var oldest = layouts.MinBy(x => x.Value.Used); layouts.Remove(oldest.Key); layoutWeight -= oldest.Value.Weight;
            }
            layouts[b.Id] = new(new(b), result, b, revision, ++useClock, weight); layoutWeight += weight;
        }
        return result;
    }
    private static int FirstVisibleLine(IReadOnlyList<TextLine> lines, float y)
    {
        var low = 0; var high = lines.Count;
        while (low < high) { var mid = low + (high - low) / 2; if (lines[mid].Bottom < y) low = mid + 1; else high = mid; }
        return low;
    }
    private void ClearLayouts() { layouts.Clear(); layoutWeight = 0; }
}
