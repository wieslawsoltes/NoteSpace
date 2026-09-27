using System.Globalization;
using System.Text;
using NoteSpace.Core;

namespace NoteSpace.Editor;

public enum NoteSearchScope { AllNotebooks, Notebook, Section, Page }
public enum NoteSearchFilter { AllContent, TagsOnly, OpenToDos, CompletedToDos }

/// <summary>Literal, ordinal search. Scope IDs are stable document IDs, never titles.</summary>
public sealed record NoteSearchQuery(string Text)
{
    public NoteSearchScope Scope { get; init; }
    public string? ScopeId { get; init; }
    public NoteSearchFilter Filter { get; init; }
    public bool MatchCase { get; init; }
    public bool WholeWord { get; init; }
    public bool IncludeTitles { get; init; } = true;
    public int MaximumResults { get; init; } = 200;
}
public readonly record struct TextMatch(int Start, int Length);
public sealed record NoteReplaceResult(int Matches, int ChangedContainers, int ChangedPages);

/// <summary>Non-overlapping UTF-16 matches. Whole-word boundaries include Unicode
/// letters, digits, combining marks and connector punctuation (including underscore).</summary>
public static class LiteralTextSearch
{
    public static IEnumerable<TextMatch> Find(string text, string query, bool matchCase = false, bool wholeWord = false)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentException.ThrowIfNullOrEmpty(query);
        return Enumerate(text, query, matchCase, wholeWord);
    }
    private static IEnumerable<TextMatch> Enumerate(string text, string query, bool matchCase, bool wholeWord)
    {
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        for (var offset = 0; offset <= text.Length - query.Length;)
        {
            var at = text.IndexOf(query, offset, comparison); if (at < 0) yield break;
            var end = at + query.Length;
            if (!SplitsPair(text, at) && !SplitsPair(text, end) && (!wholeWord || (!WordBefore(text, at) && !WordAt(text, end))))
            { yield return new(at, query.Length); offset = end; }
            else offset = at + 1;
        }
    }
    private static bool SplitsPair(string text, int at) => at > 0 && at < text.Length && char.IsHighSurrogate(text[at - 1]) && char.IsLowSurrogate(text[at]);
    private static bool WordBefore(string text, int at) => at > 0 && WordAt(text, at - (SplitsPair(text, at - 1) ? 2 : 1));
    private static bool WordAt(string text, int at)
    {
        if (at < 0 || at >= text.Length || !Rune.TryGetRuneAt(text, at, out var rune)) return false;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.ConnectorPunctuation;
    }
    internal static string Replace(string text, IReadOnlyList<TextMatch> matches, string replacement, int maximumLength)
    {
        long size = text.Length + matches.Count * (long)replacement.Length;
        foreach (var match in matches) size -= match.Length;
        if (size > maximumLength) throw new InvalidDataException("Replacement exceeds the content length limit.");
        var output = new StringBuilder((int)size); var consumed = 0;
        foreach (var match in matches) { output.Append(text, consumed, match.Start - consumed).Append(replacement); consumed = match.Start + match.Length; }
        return output.Append(text, consumed, text.Length - consumed).ToString();
    }
}

public sealed partial class EditorSession
{
    private static void ValidateQuery(NoteSearchQuery query)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentNullException.ThrowIfNull(query.Text);
        if (!Enum.IsDefined(query.Scope) || !Enum.IsDefined(query.Filter) || query.MaximumResults is < 1 or > 10000)
            throw new ArgumentOutOfRangeException(nameof(query));
        if (query.Scope != NoteSearchScope.AllNotebooks && string.IsNullOrEmpty(query.ScopeId))
            throw new ArgumentException("A scoped search requires a document ID.", nameof(query));
    }
    private IEnumerable<(Notebook Notebook, NoteSection Section, NotePage Page)> SearchPages(NoteSearchQuery query)
    {
        return Pages.Where(x => query.Scope switch {
            NoteSearchScope.AllNotebooks => true,
            NoteSearchScope.Notebook => x.Notebook.Id == query.ScopeId,
            NoteSearchScope.Section => x.Section.Id == query.ScopeId,
            NoteSearchScope.Page => x.Page.Id == query.ScopeId,
            _ => false
        });
    }
    private static bool MatchesFilter(NoteBlock block, NoteSearchFilter filter) => filter switch {
        NoteSearchFilter.OpenToDos => block.Kind == BlockKind.Checklist && !block.Checked,
        NoteSearchFilter.CompletedToDos => block.Kind == BlockKind.Checklist && block.Checked,
        _ => true
    };
    /// <summary>Stream bounded results without concatenating tables or copying note text.
    /// Cancellation is checked while scanning pages, containers and individual cells.</summary>
    public IEnumerable<SearchHit> Search(NoteSearchQuery query, CancellationToken cancellationToken = default)
    {
        ValidateQuery(query);
        return SearchCore(query, cancellationToken);
    }
    private IEnumerable<SearchHit> SearchCore(NoteSearchQuery query, CancellationToken cancellationToken)
    {
        if (query.Text.Length == 0 && query.Filter is NoteSearchFilter.AllContent or NoteSearchFilter.TagsOnly) yield break;
        var count = 0;
        bool Match(string text, out TextMatch match)
        {
            match = query.Text.Length == 0 ? new(0, 0) : LiteralTextSearch.Find(text, query.Text, query.MatchCase, query.WholeWord).FirstOrDefault(new(-1, 0));
            return match.Start >= 0;
        }
        foreach (var (notebook, section, page) in SearchPages(query))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SearchHit Hit(string text, TextMatch match, string? blockId, TableCellAddress? cell = null) =>
                new(notebook.Id, section.Id, page.Id, page.Title, Snippet(text, match.Start), blockId) {
                    Start = match.Start, Length = match.Length, Cell = cell, Location = notebook.Title + " / " + section.Title
                };
            if (query.IncludeTitles && query.Filter == NoteSearchFilter.AllContent && Match(page.Title, out var titleMatch))
            { yield return Hit(page.Title, titleMatch, null); if (++count == query.MaximumResults) yield break; }
            foreach (var block in page.Blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesFilter(block, query.Filter)) continue;
                if (query.Filter != NoteSearchFilter.TagsOnly)
                {
                    if (Match(block.Text, out var match))
                    { yield return Hit(block.Text, match, block.Id); if (++count == query.MaximumResults) yield break; continue; }
                    var matchedCell = false;
                    for (var row = 0; row < block.Cells.Count && !matchedCell; row++)
                        for (var column = 0; column < block.Cells[row].Count; column++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!Match(block.Cells[row][column], out var cellMatch)) continue;
                            yield return Hit(block.Cells[row][column], cellMatch, block.Id, new(row, column));
                            if (++count == query.MaximumResults) yield break;
                            matchedCell = true; break;
                        }
                    if (matchedCell) continue;
                }
                if (query.Filter is NoteSearchFilter.AllContent or NoteSearchFilter.TagsOnly)
                    foreach (var tag in block.Tags)
                        if (Match(tag, out _))
                        { yield return Hit(block.Text.Length == 0 ? tag : block.Text, new(-1, 0), block.Id); if (++count == query.MaximumResults) yield break; break; }
            }
        }
    }
    private static string Snippet(string text, int match)
    {
        var start = Math.Max(0, match - 30);
        if (start > 0 && start < text.Length && char.IsLowSurrogate(text[start]) && char.IsHighSurrogate(text[start - 1])) start--;
        var end = Math.Min(text.Length, start + 140);
        if (end > start && end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end--;
        return (start > 0 ? "…" : "") + text[start..end] + (end < text.Length ? "…" : "");
    }
    /// <summary>Replace note text and table cells in one rollback-safe history action.
    /// Titles, filenames and tag labels are deliberately unchanged. Result limits do
    /// not restrict replacement; all matches in the selected scope are processed.</summary>
    public NoteReplaceResult ReplaceAll(NoteSearchQuery query, string replacement, CancellationToken cancellationToken = default, Action<NoteBlock>? reflow = null)
    {
        ValidateQuery(query); ArgumentException.ThrowIfNullOrEmpty(query.Text); ArgumentNullException.ThrowIfNull(replacement);
        if (query.Filter == NoteSearchFilter.TagsOnly) throw new ArgumentException("Tag labels are not replaced by text replacement.", nameof(query));
        var matches = 0; var containers = 0; var pages = 0;
        Execute("Replace all", _ => {
            foreach (var (_, _, page) in SearchPages(query))
            {
                cancellationToken.ThrowIfCancellationRequested(); var pageChanged = false;
                foreach (var block in page.Blocks)
                {
                    cancellationToken.ThrowIfCancellationRequested(); if (!MatchesFilter(block, query.Filter)) continue;
                    var before = block.Text; var blockChanged = false;
                    matches += RichText.ReplaceAll(block, query.Text, replacement, query.MatchCase, query.WholeWord);
                    if (before != block.Text) { reflow?.Invoke(block); containers++; blockChanged = true; }
                    foreach (var row in block.Cells) for (var column = 0; column < row.Count; column++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var ranges = LiteralTextSearch.Find(row[column], query.Text, query.MatchCase, query.WholeWord).ToArray();
                        if (ranges.Length == 0) continue; matches += ranges.Length;
                        var value = LiteralTextSearch.Replace(row[column], ranges, replacement, NoteTable.MaximumCellLength);
                        if (value == row[column]) continue;
                        row[column] = value; containers++; blockChanged = true;
                    }
                    pageChanged |= blockChanged;
                }
                if (pageChanged) { page.Modified = DateTimeOffset.Now; pages++; }
            }
        });
        return new(matches, containers, pages);
    }
}
