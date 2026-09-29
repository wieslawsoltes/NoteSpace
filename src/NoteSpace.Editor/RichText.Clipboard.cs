using NoteSpace.Core;

namespace NoteSpace.Editor;

public static partial class RichText
{
    public static NoteBlock CopyRange(NoteBlock source, int start, int length)
    {
        CheckRange(source.Text, start, length);
        var end = start + length;
        var slice = new NoteBlock { Text = source.Text.Substring(start, length), Format = Copy(source.Format), TextFlow = source.TextFlow.Copy() };
        var runs = new List<TextRun>();
        foreach (var run in Resolve(source))
        {
            var a = Math.Max(start, run.Start); var b = Math.Min(end, run.End);
            if (b > a) Append(runs, new(a - start, b - a, run.Format, run.Link));
        }
        slice.Marks = ToMarks(runs, slice.Format); return slice;
    }
    /// <summary>Replace a range with a detached rich-text fragment in one run pass.
    /// Existing text and links outside the range retain their original attributes.</summary>
    public static void PasteRange(NoteBlock target, int start, int length, NoteBlock fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment); CheckRange(target.Text, start, length);
        // Clipboard payloads can come from a host, not only CopyRange.
        DocumentJson.Validate(new Workspace { Notebooks = [new Notebook { Sections = [new NoteSection { Pages = [new NotePage { Blocks = [fragment] }] }] }] });
        var newLength = target.Text.Length - (long)length + fragment.Text.Length;
        if (newLength > MaximumTextLength) throw new InvalidDataException("Text exceeds the 2 MiB container limit.");
        var old = Resolve(target); var runs = new List<TextRun>(); var end = start + length;
        foreach (var run in old)
        {
            var to = Math.Min(run.End, start);
            if (to > run.Start) Append(runs, run with { Length = to - run.Start });
        }
        foreach (var run in Resolve(fragment)) Append(runs, run with { Start = start + run.Start });
        foreach (var run in old)
        {
            var from = Math.Max(end, run.Start);
            if (run.End > from) Append(runs, run with { Start = from - length + fragment.Text.Length, Length = run.End - from });
        }
        var marks = ToMarks(runs, target.Format);
        var text = string.Concat(target.Text.AsSpan(0, start), fragment.Text.AsSpan(), target.Text.AsSpan(end));
        target.Text = text; target.Marks = marks;
    }
}
