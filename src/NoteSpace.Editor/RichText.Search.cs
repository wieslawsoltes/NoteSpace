using NoteSpace.Core;

namespace NoteSpace.Editor;

public static partial class RichText
{
    /// <summary>Literal whole-word replacement in one run pass. Existing run styles
    /// between matches are retained; each replacement inherits its occurrence's style.</summary>
    public static int ReplaceAll(NoteBlock block, string find, string replacement, bool matchCase, bool wholeWord)
    {
        ArgumentNullException.ThrowIfNull(block); ArgumentNullException.ThrowIfNull(replacement);
        var matches = LiteralTextSearch.Find(block.Text, find, matchCase, wholeWord).Select(m => (m.Start, m.Length)).ToArray();
        if (matches.Length > 0 && (find.Length != replacement.Length || matches.Any(m => !block.Text.AsSpan(m.Start, m.Length).SequenceEqual(replacement.AsSpan())))) ReplaceMatches(block, matches, replacement);
        return matches.Length;
    }
}
