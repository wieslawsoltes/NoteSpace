using NoteSpace.Editor;

namespace NoteSpace.Controls;

public sealed partial class NoteSurface
{
    /// <summary>Reveal a result by stable IDs, then select its exact native text range.
    /// No document content is changed. Stale matches are clamped to current text.</summary>
    public void RevealSearchResult(SearchHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        if (session?.FindPage(hit.PageId) is null) return;
        NavigateToPage(hit.PageId, hit.BlockId); if (HasPendingText) return;
        if (hit.Cell is { } cell && SelectedBlock is { } table) BeginEditTableCell(table, cell);
        else if (hit.Start >= 0)
        {
            if (hit.BlockId is null) BeginEditTitle();
            else if (SelectedBlock is { } block) BeginEdit(block);
        }
        if (editor is not null && hit.Start >= 0)
        {
            var start = Math.Clamp(hit.Start, 0, editor.Text.Length);
            var end = start + Math.Clamp(hit.Length, 0, editor.Text.Length - start);
            if (richDraft is not null) SetRichSelection(start, end);
            else editor.Select(start, end - start);
        }
    }
}
