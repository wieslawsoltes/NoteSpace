using System.Text;
using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed record TextRun(int Start, int Length, TextFormat Format, string? Link)
{
    public int End => Start + Length;
}

/// <summary>UTF-16 range editing with resolved, non-overlapping formatting runs.</summary>
public static partial class RichText
{
    private const int MaximumTextLength = 2 * 1024 * 1024;

    public static TextFormat At(NoteBlock block, int offset) =>
        block.Marks.LastOrDefault(m => offset >= m.Start && offset < (long)m.Start + m.Length)?.Format ?? block.Format;

    /// <summary>Resolve legacy overlapping marks with last-mark-wins semantics in
    /// O(m log m), where m is the number of marks. Returned formats are detached.</summary>
    public static IReadOnlyList<TextRun> GetRuns(NoteBlock block) =>
        Resolve(block).Select(r => r with { Format = Copy(r.Format) }).ToArray();

    private static List<TextRun> Resolve(NoteBlock block)
    {
        var events = new List<(int Offset, int Mark, bool Start)>(block.Marks.Count * 2 + 2) { (0, -1, true), (block.Text.Length, -1, false) };
        for (var i = 0; i < block.Marks.Count; i++)
        {
            var mark = block.Marks[i];
            if (mark.Length == 0) continue;
            events.Add((mark.Start, i, true)); events.Add((mark.Start + mark.Length, i, false));
        }
        events.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        var active = new SortedSet<int>(); var result = new List<TextRun>(); var position = 0; var e = 0;
        while (e < events.Count)
        {
            var next = events[e].Offset;
            if (next > position)
            {
                var mark = active.Count == 0 ? null : block.Marks[active.Max];
                Append(result, new(position, next - position, mark?.Format ?? block.Format, mark?.Link));
            }
            while (e < events.Count && events[e].Offset == next)
            {
                var change = events[e++]; if (change.Mark < 0) continue;
                if (change.Start) active.Add(change.Mark); else active.Remove(change.Mark);
            }
            position = next;
        }
        return result;
    }

    public static bool AllHave(NoteBlock block, int start, int length, Func<TextFormat, bool> predicate)
    {
        CheckRange(block.Text, start, length);
        if (length == 0) { start = 0; length = block.Text.Length; }
        return length == 0 ? predicate(block.Format) : Resolve(block).Where(r => r.Start < start + length && r.End > start).All(r => predicate(r.Format));
    }

    /// <summary>Format every intersecting style run without flattening its other
    /// attributes. A zero-length range applies to the entire container. Null links
    /// preserve existing links; an empty link explicitly removes them.</summary>
    public static void Apply(NoteBlock block, int start, int length, Action<TextFormat> format, string? link = null)
    {
        ArgumentNullException.ThrowIfNull(format); CheckRange(block.Text, start, length);
        var entireContainer = length == 0;
        if (entireContainer) { start = 0; length = block.Text.Length; }
        var end = start + length; var baseFormat = Copy(block.Format);
        if (entireContainer) format(baseFormat);
        var output = new List<TextRun>();
        foreach (var run in Resolve(block))
        {
            if (run.End <= start || run.Start >= end) { Append(output, run); continue; }
            if (run.Start < start) Append(output, run with { Length = start - run.Start });
            var from = Math.Max(start, run.Start); var to = Math.Min(end, run.End);
            var changed = Copy(run.Format); format(changed);
            Append(output, new(from, to - from, changed, link is null ? run.Link : link.Length == 0 ? null : link));
            if (run.End > end) Append(output, run with { Start = end, Length = run.End - end });
        }
        var marks = ToMarks(output, baseFormat);
        block.Format = baseFormat; block.Marks = marks;
    }

    // Native input supplies the complete value. Find its minimal changed range;
    // exact replacements and Replace All use the explicit range API instead.
    public static void Replace(NoteBlock block, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var old = block.Text; if (old == value) return;
        var prefix = 0;
        while (prefix < old.Length && prefix < value.Length && old[prefix] == value[prefix]) prefix++;
        if (SplitsPair(old, prefix) || SplitsPair(value, prefix)) prefix--;
        var suffix = 0;
        while (suffix < old.Length - prefix && suffix < value.Length - prefix && old[old.Length - 1 - suffix] == value[value.Length - 1 - suffix]) suffix++;
        if (SplitsPair(old, old.Length - suffix) || SplitsPair(value, value.Length - suffix)) suffix--;
        ReplaceRange(block, prefix, old.Length - prefix - suffix, value.Substring(prefix, value.Length - prefix - suffix));
    }

    public static void ReplaceRange(NoteBlock block, int start, int length, string value)
    {
        ArgumentNullException.ThrowIfNull(value); CheckRange(block.Text, start, length);
        if (length == 0 && value.Length == 0) return;
        ReplaceMatches(block, [(start, length)], value);
    }

    /// <summary>Insert explicitly formatted typing without flattening surrounding runs.</summary>
    public static void ReplaceRange(NoteBlock block, int start, int length, string value, TextFormat typingFormat, string? link = null)
    {
        ArgumentNullException.ThrowIfNull(value); ArgumentNullException.ThrowIfNull(typingFormat);
        CheckRange(block.Text, start, length);
        if (length == 0 && value.Length == 0) return;
        ReplaceMatches(block, [(start, length)], value, Copy(typingFormat), link);
    }

    public static TextFormat CloneStyle(TextFormat format) => Copy(format);

    public static void CopyStyle(TextFormat source, TextFormat target)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(target);
        target.FontFamily = source.FontFamily; target.FontSize = source.FontSize;
        target.Bold = source.Bold; target.Italic = source.Italic; target.Underline = source.Underline;
        target.Strike = source.Strike; target.Baseline = source.Baseline;
        target.Color = source.Color; target.Highlight = source.Highlight;
        target.Alignment = source.Alignment; target.Bullets = source.Bullets; target.Numbered = source.Numbered;
    }

    /// <summary>Replace non-overlapping ordinal matches in one text/run pass, retaining
    /// styles between matches rather than treating them as one large replacement.</summary>
    public static int ReplaceAll(NoteBlock block, string find, string replacement, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        ArgumentException.ThrowIfNullOrEmpty(find); ArgumentNullException.ThrowIfNull(replacement);
        if (comparison is not (StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase)) throw new ArgumentOutOfRangeException(nameof(comparison));
        var matches = new List<(int Start, int Length)>(); var offset = 0;
        while (offset <= block.Text.Length - find.Length)
        {
            var at = block.Text.IndexOf(find, offset, comparison); if (at < 0) break;
            CheckRange(block.Text, at, find.Length); matches.Add((at, find.Length)); offset = at + find.Length;
        }
        if (matches.Count > 0) ReplaceMatches(block, matches, replacement);
        return matches.Count;
    }

    private static void ReplaceMatches(NoteBlock block, IReadOnlyList<(int Start, int Length)> matches, string replacement, TextFormat? typingFormat = null, string? typingLink = null)
    {
        long length = block.Text.Length;
        foreach (var match in matches) length += replacement.Length - (long)match.Length;
        if (length > MaximumTextLength) throw new InvalidDataException("Text exceeds the 2 MiB container limit.");
        var runs = Resolve(block); var output = new List<TextRun>(); var text = new StringBuilder((int)length); var cursor = 0;
        void Original(int from, int to)
        {
            while (cursor < runs.Count && runs[cursor].End <= from) cursor++;
            while (from < to)
            {
                var run = runs[cursor]; var end = Math.Min(to, run.End); var at = text.Length;
                text.Append(block.Text, from, end - from); Append(output, new(at, end - from, run.Format, run.Link));
                from = end; if (from == run.End) cursor++;
            }
        }
        var consumed = 0;
        foreach (var match in matches)
        {
            Original(consumed, match.Start);
            var probe = match.Length == 0 && match.Start > 0 ? match.Start - 1 : match.Start;
            var styleIndex = cursor;
            if (styleIndex >= runs.Count || styleIndex > 0 && runs[styleIndex].Start > probe) styleIndex--;
            var inherited = styleIndex >= 0 && styleIndex < runs.Count ? runs[styleIndex] : null;
            var link = inherited?.Link;
            if (match.Length == 0 && inherited is not null && (match.Start == inherited.Start || match.Start == inherited.End)) link = null;
            var at = text.Length; text.Append(replacement);
            Append(output, new(at, replacement.Length, typingFormat ?? inherited?.Format ?? block.Format, typingFormat is not null ? typingLink : link));
            consumed = match.Start + match.Length;
        }
        Original(consumed, block.Text.Length);
        var marks = ToMarks(output, block.Format);
        block.Text = text.ToString(); block.Marks = marks;
    }

    private static void CheckRange(string text, int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > text.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (SplitsPair(text, start) || SplitsPair(text, start + length)) throw new ArgumentException("A text range cannot split a Unicode surrogate pair.");
    }
    private static bool SplitsPair(string text, int offset) => offset > 0 && offset < text.Length && char.IsHighSurrogate(text[offset - 1]) && char.IsLowSurrogate(text[offset]);
    private static void Append(List<TextRun> runs, TextRun run)
    {
        if (run.Length <= 0) return;
        if (runs.Count > 0 && runs[^1].End == run.Start && runs[^1].Link == run.Link && Same(runs[^1].Format, run.Format))
            runs[^1] = runs[^1] with { Length = runs[^1].Length + run.Length };
        else runs.Add(run);
    }
    private static List<TextMark> ToMarks(List<TextRun> runs, TextFormat baseFormat)
    {
        var result = runs.Where(r => r.Link is not null || !Same(r.Format, baseFormat))
            .Select(r => new TextMark { Start = r.Start, Length = r.Length, Format = Copy(r.Format), Link = r.Link }).ToList();
        if (result.Count > 10000) throw new InvalidDataException("Formatting exceeds the 10,000-run container limit.");
        return result;
    }
    private static bool Same(TextFormat a, TextFormat b) => a.FontFamily == b.FontFamily && a.FontSize == b.FontSize && a.Bold == b.Bold && a.Italic == b.Italic && a.Underline == b.Underline && a.Strike == b.Strike && a.Baseline == b.Baseline && a.Color == b.Color && a.Highlight == b.Highlight && a.Alignment == b.Alignment && a.Bullets == b.Bullets && a.Numbered == b.Numbered;
    private static TextFormat Copy(TextFormat f) => new() { FontFamily = f.FontFamily, FontSize = f.FontSize, Bold = f.Bold, Italic = f.Italic, Underline = f.Underline, Strike = f.Strike, Baseline = f.Baseline, Color = f.Color, Highlight = f.Highlight, Alignment = f.Alignment, Bullets = f.Bullets, Numbered = f.Numbered };
}
