using NoteSpace.Core;

namespace NoteSpace.Editor;

public sealed partial class EditorSession
{
    private IEnumerable<(Notebook Notebook, NoteSection Section, NotePage Page)> SearchPages(NoteSearchQuery query)
    {
        // Generic transaction callbacks may reorganize collections. Their queries
        // must see the live graph, not an index built before the transaction.
        if (executing)
        {
            foreach (var location in EnumeratePages())
                if (query.Scope switch {
                    NoteSearchScope.AllNotebooks => true,
                    NoteSearchScope.Notebook => location.Notebook.Id == query.ScopeId,
                    NoteSearchScope.Section => location.Section.Id == query.ScopeId,
                    NoteSearchScope.Page => location.Page.Id == query.ScopeId,
                    _ => false
                }) yield return location;
            yield break;
        }
        if (query.Scope == NoteSearchScope.AllNotebooks)
        {
            foreach (var location in Pages) yield return location;
            yield break;
        }
        if (query.Scope == NoteSearchScope.Page)
        {
            EnsureIndexes();
            if (pageIndex!.TryGetValue(query.ScopeId!, out var location)) yield return location;
            yield break;
        }
        if (query.Scope == NoteSearchScope.Section)
        {
            var section = FindSection(query.ScopeId);
            if (section is null) yield break;
            var owner = Document.Notebooks.FirstOrDefault(n => n.Sections.Contains(section));
            if (owner is null) yield break;
            foreach (var page in section.Pages) yield return (owner, section, page);
            yield break;
        }
        if (query.Scope == NoteSearchScope.Notebook)
        {
            var notebook = Document.Notebooks.FirstOrDefault(n => n.Id == query.ScopeId);
            if (notebook is null) yield break;
            foreach (var section in notebook.Sections)
                foreach (var page in section.Pages) yield return (notebook, section, page);
        }
    }
}
