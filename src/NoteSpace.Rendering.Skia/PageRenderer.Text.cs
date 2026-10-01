using System.Globalization;
using System.Text.RegularExpressions;
using NoteSpace.Core;
using NoteSpace.Editor;

namespace NoteSpace.Rendering.Skia;

public sealed partial class PageRenderer
{
    private readonly record struct StyleKey(string Family, float Size, bool Bold, bool Italic, bool Underline, bool Strike, int Baseline,
        uint Color, uint Highlight, int Alignment, bool Bullets, bool Numbered)
    {
        public static StyleKey From(TextFormat f) => new(f.FontFamily, f.FontSize, f.Bold, f.Italic, f.Underline, f.Strike, f.Baseline, f.Color, f.Highlight, f.Alignment, f.Bullets, f.Numbered);
    }
    private sealed class LayoutStamp(NoteBlock block)
    {
        private readonly (float, float, float, float, float, float, float) flow = FlowKey(block.TextFlow);
        private static (float, float, float, float, float, float, float) FlowKey(TextFlowSettings f) =>
            (f.LeftIndent, f.FirstLineIndent, f.RightIndent, f.SpaceBefore, f.SpaceAfter, f.LineSpacing, f.TabWidth);
        private readonly string text = block.Text;
        private readonly float width = block.Width;
        private readonly BlockKind kind = block.Kind;
        private readonly StyleKey style = StyleKey.From(block.Format);
        private readonly (int Start, int Length, StyleKey Style)[] marks = block.Marks.Select(m => (m.Start, m.Length, StyleKey.From(m.Format))).ToArray();
        public bool Matches(NoteBlock b)
        {
            // Strings are immutable. Reference checks avoid re-hashing megabytes on every pan.
            if (flow != FlowKey(b.TextFlow) || !ReferenceEquals(text, b.Text) || width != b.Width || kind != b.Kind || style != StyleKey.From(b.Format) || marks.Length != b.Marks.Count) return false;
            for (var i = 0; i < marks.Length; i++)
            {
                var m = b.Marks[i]; if (marks[i] != (m.Start, m.Length, StyleKey.From(m.Format))) return false;
            }
            return true;
        }
    }
    private sealed class CachedLayout(LayoutStamp stamp, TextLayout value, NoteBlock block, long? revision, long used, long weight)
    {
        public LayoutStamp Stamp { get; } = stamp;
        public TextLayout Value { get; } = value;
        public NoteBlock Block { get; set; } = block;
        public long? Revision { get; set; } = revision;
        public long Used { get; set; } = used;
        public long Weight { get; } = weight;
    }
    private readonly Dictionary<string, CachedLayout> layouts = new();
    private long layoutWeight;
    private const long MaximumLayoutWeight = 8 * 1024 * 1024;
    private static readonly Regex Tokens = new(@"\r\n|\r|\n|\t|[^\S\r\n\t]+|[^\s]+", RegexOptions.CultureInvariant);

    public TextLayout Layout(NoteBlock block) => LayoutCore(block, null);
    private TextLayout LayoutCore(NoteBlock b, long? revision)
    {
        ObjectDisposedException.ThrowIf(disposed, this); ArgumentNullException.ThrowIfNull(b);
        if (layouts.TryGetValue(b.Id, out var cached)
            && (revision.HasValue && cached.Revision == revision && ReferenceEquals(cached.Block, b) || cached.Stamp.Matches(b)))
        {
            cached.Used = ++useClock; cached.Revision = revision; cached.Block = b;
            Statistics.LayoutHits++; return cached.Value;
        }
        Statistics.LayoutBuilds++;
        var fragments = new List<TextFragment>(); var lines = new List<TextLine>();
        var baseFormat = RichText.CloneStyle(b.Format); var flow = b.TextFlow;
        var indent = (b.Kind == BlockKind.Checklist || baseFormat.Bullets || baseFormat.Numbered ? 24f : 0f) + flow.LeftIndent;
        var paragraphFirst = true; var paragraphNumber = 1;
        var left = 12 + indent + flow.FirstLineIndent;
        var width = Math.Max(12, b.Width - left - 12 - flow.RightIndent);
        var x = 0f; var y = flow.SpaceBefore; var lineHeight = baseFormat.FontSize * flow.LineSpacing;
        var line = new List<(string Text, int Start, float X, float Width, TextFormat Format)>();
        var lineStart = 0;
        void Flush(int end, int nextStart, bool hardBreak)
        {
            var first = fragments.Count;
            var shift = baseFormat.Alignment == 1 ? Math.Max(0, (width - x) / 2) : baseFormat.Alignment == 2 ? Math.Max(0, width - x) : 0;
            var baseline = y + lineHeight - 5;
            if (paragraphFirst && (baseFormat.Bullets || baseFormat.Numbered))
                fragments.Add(new(baseFormat.Numbered ? paragraphNumber.ToString() + "." : "•", 12 + flow.LeftIndent, baseline, 20, baseFormat));
            foreach (var f in line)
            {
                var shiftY = f.Format.Baseline > 0 ? -f.Format.FontSize * 0.35f : f.Format.Baseline < 0 ? f.Format.FontSize * 0.18f : 0;
                fragments.Add(new(f.Text, left + f.X + shift, baseline + shiftY, f.Width, f.Format) { SourceStart = f.Start });
            }
            lines.Add(new(first, fragments.Count - first, y, y + lineHeight, lineStart, end, nextStart, left + shift));
            y += lineHeight; x = 0; line.Clear(); lineHeight = baseFormat.FontSize * flow.LineSpacing;
            paragraphFirst = hardBreak;
            if (hardBreak) { y += flow.SpaceAfter + flow.SpaceBefore; paragraphNumber++; }
            left = 12 + indent + (paragraphFirst ? flow.FirstLineIndent : 0);
            width = Math.Max(12, b.Width - left - 12 - flow.RightIndent); lineStart = nextStart;
        }
        void Piece(string text, int start, TextFormat format)
        {
            var font = Font(format);
            var measured = text == "\t" ? (MathF.Floor(x / flow.TabWidth) + 1) * flow.TabWidth - x : font.MeasureText(text);
            if (x > 0 && x + measured > width && !string.IsNullOrWhiteSpace(text)) Flush(start, start, false);
            if (measured <= width || text == "\t")
            {
                line.Add((text, start, x, measured, format)); x += measured; lineHeight = Math.Max(lineHeight, format.FontSize * flow.LineSpacing); return;
            }
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                var value = elements.GetTextElement(); var advance = font.MeasureText(value); var at = start + elements.ElementIndex;
                if (x > 0 && x + advance > width) Flush(at, at, false);
                line.Add((value, at, x, advance, format)); x += advance; lineHeight = Math.Max(lineHeight, format.FontSize * flow.LineSpacing);
            }
        }
        var runs = RichText.GetRuns(b); var runIndex = 0;
        foreach (Match token in Tokens.Matches(b.Text))
        {
            if (token.Value is "\n" or "\r" or "\r\n") { Flush(token.Index, token.Index + token.Length, true); continue; }
            var at = token.Index; var end = at + token.Length;
            while (at < end)
            {
                while (runIndex < runs.Count && runs[runIndex].End <= at) runIndex++;
                var run = runs[runIndex]; var to = Math.Min(end, run.End);
                Piece(b.Text[at..to], at, run.Format); at = to;
            }
        }
        Flush(b.Text.Length, b.Text.Length, false);
        var result = new TextLayout(fragments, y + flow.SpaceAfter + 24) { Lines = lines };
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
