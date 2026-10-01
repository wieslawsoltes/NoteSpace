using System.Globalization;
using NoteSpace.Core;

namespace NoteSpace.Editor;

/// <summary>A detached rich-text input draft. Selection and future-typing styles are
/// transient; the host commits content through its own transaction/undo policy.
/// One buffer belongs to one writer. It never holds or mutates a workspace.</summary>
public sealed class TextEditingBuffer
{
    private int[]? boundaries;
    private IReadOnlyList<TextRun>? attributeRuns;
    private long attributeVersion = -1;
    private TextFormat typingFormat;
    private string? typingLink;
    public NoteBlock Block { get; }
    public int Anchor { get; private set; }
    public int Caret { get; private set; }
    public int SelectionStart => Math.Min(Anchor, Caret);
    public int SelectionLength => Math.Abs(Anchor - Caret);
    public long Version { get; private set; }
    public TextFormat CurrentFormat => RichText.CloneStyle(typingFormat);
    public TextEditingBuffer(NoteBlock source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Kind is not (BlockKind.Text or BlockKind.Heading or BlockKind.Checklist))
            throw new ArgumentException("Select a text container.", nameof(source));
        Block = DocumentJson.CloneBlock(source);
        typingFormat = RichText.CloneStyle(source.Format);
        Select(source.Text.Length, source.Text.Length);
    }
    private int[] Boundaries => boundaries ??= StringInfo.ParseCombiningCharacters(Block.Text).Append(Block.Text.Length).ToArray();
    /// <summary>Snap native selections to complete graphemes. CRLF, surrogate pairs
    /// and combining/emoji sequences stay intact when moving or deleting.</summary>
    public int Snap(int offset)
    {
        offset = Math.Clamp(offset, 0, Block.Text.Length);
        if (offset == 0 || offset == Block.Text.Length) return offset;
        var at = Array.BinarySearch(Boundaries, offset);
        return at >= 0 ? offset : Boundaries[Math.Max(0, ~at - 1)];
    }
    public void Select(int anchor, int caret, bool preserveTypingFormat = false)
    {
        Anchor = Snap(anchor); Caret = Snap(caret);
        if (!preserveTypingFormat) InheritStyle();
    }
    private void InheritStyle()
    {
        var at = SelectionLength > 0 ? SelectionStart : Caret > 0 ? Caret - 1 : 0;
        typingFormat = RichText.CloneStyle(RichText.At(Block, at));
        var mark = Block.Marks.LastOrDefault(m => at >= m.Start && at < m.Start + m.Length);
        typingLink = mark?.Link;
        if (SelectionLength == 0 && mark is not null && (Caret == mark.Start || Caret == mark.Start + mark.Length)) typingLink = null;
    }
    public int Adjacent(int offset, int direction, bool word = false)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var at = Array.BinarySearch(Boundaries, Snap(offset));
        if (!word) return Boundaries[Math.Clamp(at + direction, 0, Boundaries.Length - 1)];
        bool Space(int index) => index >= 0 && index < Boundaries.Length - 1 && char.IsWhiteSpace(Block.Text, Boundaries[index]);
        bool Word(int index) => index >= 0 && index < Boundaries.Length - 1 && (char.IsLetterOrDigit(Block.Text, Boundaries[index]) || Block.Text[Boundaries[index]] == '_');
        if (direction > 0)
        {
            var category = Word(at);
            while (at < Boundaries.Length - 1 && !Space(at) && Word(at) == category) at++;
            while (Space(at)) at++;
        }
        else
        {
            while (at > 0 && Space(at - 1)) at--;
            var category = Word(at - 1);
            while (at > 0 && !Space(at - 1) && Word(at - 1) == category) at--;
        }
        return Boundaries[at];
    }
    public void SelectWord(int offset)
    {
        var at = Snap(offset);
        if (at == Block.Text.Length && at > 0) at = Adjacent(at, -1);
        var end = Adjacent(at, 1, true);
        // Do not include following spaces in a word double-click selection.
        while (end > at && char.IsWhiteSpace(Block.Text, Adjacent(end, -1))) end = Adjacent(end, -1);
        var start = end > 0 ? Adjacent(end, -1, true) : 0;
        Select(start, Math.Max(at, end));
    }
    public void Format(Action<TextFormat> apply, string? link = null)
    {
        ArgumentNullException.ThrowIfNull(apply);
        var next = RichText.CloneStyle(typingFormat); apply(next);
        ValidateFormat(next);
        if (SelectionLength > 0)
        {
            RichText.Apply(Block, SelectionStart, SelectionLength, apply, link); Version++;
        }
        typingFormat = next;
        if (link is not null) typingLink = link.Length == 0 ? null : link;
    }
    public bool AllHave(Func<TextFormat, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (SelectionLength == 0) return predicate(typingFormat);
        // Ribbon state asks several attribute questions for each selection change.
        // Resolve overlapping marks once per content version, not once per button.
        if (attributeVersion != Version || attributeRuns is null)
        {
            attributeRuns = RichText.GetRuns(Block); attributeVersion = Version;
        }
        var end = SelectionStart + SelectionLength;
        for (var i = 0; i < attributeRuns.Count; i++)
        {
            var run = attributeRuns[i];
            if (run.Start >= end) break;
            if (run.End > SelectionStart && !predicate(run.Format)) return false;
        }
        return true;
    }
    public void FormatContainer(Action<TextFormat> apply)
    {
        var probe = RichText.CloneStyle(Block.Format); apply(probe); ValidateFormat(probe);
        RichText.Apply(Block, 0, 0, apply); Version++; InheritStyle();
    }
    public void SetFlow(TextFlowSettings flow)
    {
        ArgumentNullException.ThrowIfNull(flow); flow.Validate(); Block.TextFlow = flow.Copy(); Version++;
    }
    public void ReplaceSelection(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var start = SelectionStart; var length = SelectionLength;
        if (length == 0 && text.Length == 0) return;
        RichText.ReplaceRange(Block, start, length, text, typingFormat, typingLink);
        boundaries = null; Version++; Select(start + text.Length, start + text.Length, true);
    }
    /// <summary>Reconcile native text/IME/clipboard input using selection before minimal difference.
    /// Content is assigned only after range/size validation succeeds.</summary>
    public void AcceptText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var old = Block.Text; if (old == value) return;
        // Prefer the actual selected range. A minimal string diff is ambiguous in
        // repeated text (inserting "a" into "aaaa" otherwise formats the final a).
        var start = SelectionStart; var length = SelectionLength;
        var insertedLength = value.Length - (old.Length - length);
        if (insertedLength >= 0 && start + insertedLength <= value.Length
            && old.AsSpan(0, start).SequenceEqual(value.AsSpan(0, start))
            && old.AsSpan(start + length).SequenceEqual(value.AsSpan(start + insertedLength)))
        {
            RichText.ReplaceRange(Block, start, length, value.Substring(start, insertedLength), typingFormat, typingLink);
            boundaries = null; Version++; Select(start + insertedLength, start + insertedLength, true); return;
        }
        var prefix = 0;
        while (prefix < old.Length && prefix < value.Length && old[prefix] == value[prefix]) prefix++;
        if (SplitsPair(old, prefix) || SplitsPair(value, prefix)) prefix--;
        var suffix = 0;
        while (suffix < old.Length - prefix && suffix < value.Length - prefix && old[old.Length - 1 - suffix] == value[value.Length - 1 - suffix]) suffix++;
        if (SplitsPair(old, old.Length - suffix) || SplitsPair(value, value.Length - suffix)) suffix--;
        var inserted = value.Substring(prefix, value.Length - prefix - suffix);
        RichText.ReplaceRange(Block, prefix, old.Length - prefix - suffix, inserted, typingFormat, typingLink);
        boundaries = null; Version++; Select(prefix + inserted.Length, prefix + inserted.Length, true);
    }
    public void Delete(bool backwards, bool word = false)
    {
        if (SelectionLength == 0) Select(Caret, Adjacent(Caret, backwards ? -1 : 1, word), true);
        ReplaceSelection("");
    }
    public NoteBlock CopySelection() => RichText.CopyRange(Block, SelectionStart, SelectionLength);
    public void Paste(NoteBlock fragment)
    {
        var start = SelectionStart;
        RichText.PasteRange(Block, start, SelectionLength, fragment);
        boundaries = null; Version++; Select(start + fragment.Text.Length, start + fragment.Text.Length);
    }
    public void CopyContentTo(NoteBlock target)
    {
        if (target.Id != Block.Id) throw new ArgumentException("Draft and target identities differ.", nameof(target));
        target.Text = Block.Text; target.Format = RichText.CloneStyle(Block.Format);
        target.Marks = Block.Marks.Select(m => new TextMark { Start = m.Start, Length = m.Length, Link = m.Link, Format = RichText.CloneStyle(m.Format) }).ToList();
        target.TextFlow = Block.TextFlow.Copy(); target.Height = Block.Height;
    }
    private static bool SplitsPair(string text, int at) => at > 0 && at < text.Length && char.IsHighSurrogate(text[at - 1]) && char.IsLowSurrogate(text[at]);
    private static void ValidateFormat(TextFormat f)
    {
        if (f.FontFamily is null || f.FontFamily.Length > 200 || !float.IsFinite(f.FontSize) || f.FontSize is < 6 or > 144 || f.Alignment is < 0 or > 2 || f.Baseline is < -1 or > 1)
            throw new InvalidDataException("Invalid text formatting.");
    }
}
