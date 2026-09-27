namespace NoteSpace.Core;

/// <summary>A group contains sections and other groups through notebook-local IDs.
/// Sections stay in Notebook.Sections so existing page enumeration remains lossless.</summary>
public sealed class SectionGroup
{
    public string Id { get; set; } = Ids.New();
    public string Title { get; set; } = "New section group";
    public string? ParentId { get; set; }
    public uint Color { get; set; } = 0xFF9262B8;
    public bool IsCollapsed { get; set; }
}

public sealed record NotebookOutlineEntry(string Id, int Level, SectionGroup? Group, NoteSection? Section);

/// <summary>UI-independent group validation, paths and navigation projections.</summary>
public static class NotebookGroups
{
    public const int MaximumDepth = 8;
    public const int MaximumGroups = 1000;

    public static void Validate(Notebook notebook)
    {
        if (notebook.SectionGroups is null || notebook.SectionGroups.Count > MaximumGroups)
            throw new InvalidDataException("Invalid section group collection.");
        var groups = new Dictionary<string, SectionGroup>(StringComparer.Ordinal);
        foreach (var group in notebook.SectionGroups)
            if (group is null || string.IsNullOrWhiteSpace(group.Id) || group.Id.Length > 100
                || group.Title is null || group.Title.Length > 500 || !groups.TryAdd(group.Id, group))
                throw new InvalidDataException("Invalid or duplicate section group.");
        foreach (var group in groups.Values)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal); SectionGroup? current = group;
            while (current is not null)
            {
                if (!visited.Add(current.Id)) throw new InvalidDataException("Section groups cannot contain cycles.");
                if (visited.Count > MaximumDepth) throw new InvalidDataException($"Section groups support at most {MaximumDepth} levels.");
                if (current.ParentId is not { } parent) break;
                if (!groups.TryGetValue(parent, out current)) throw new InvalidDataException("Section group parent must belong to the same notebook.");
            }
        }
        if (notebook.Sections is null) throw new InvalidDataException("Invalid section collection.");
        foreach (var section in notebook.Sections)
            if (section is null || section.GroupId is { } id && !groups.ContainsKey(id))
                throw new InvalidDataException("Section group must belong to the section's notebook.");
    }

    /// <summary>Root-to-leaf group IDs. Queries never expand or otherwise mutate groups.</summary>
    public static IReadOnlyList<string> Ancestors(Notebook notebook, string? groupId)
    {
        var groups = notebook.SectionGroups.ToDictionary(g => g.Id, StringComparer.Ordinal);
        var path = new List<string>();
        while (groupId is not null)
        {
            if (path.Count == MaximumDepth || path.Contains(groupId) || !groups.TryGetValue(groupId, out var group))
                throw new InvalidDataException("Invalid section group path.");
            path.Add(groupId); groupId = group.ParentId;
        }
        path.Reverse(); return path;
    }

    public static string Path(Notebook notebook, string? groupId = null)
    {
        var titles = notebook.SectionGroups.ToDictionary(g => g.Id, g => g.Title, StringComparer.Ordinal);
        return string.Join(" / ", new[] { notebook.Title }.Concat(Ancestors(notebook, groupId).Select(id => titles[id])));
    }

    public static HashSet<string> SubtreeIds(Notebook notebook, string groupId)
    {
        Validate(notebook);
        if (!notebook.SectionGroups.Any(g => g.Id == groupId)) throw new ArgumentException("Section group not found.", nameof(groupId));
        var children = notebook.SectionGroups.ToLookup(g => g.ParentId ?? "", StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal); var pending = new Stack<string>(); pending.Push(groupId);
        while (pending.TryPop(out var id)) { result.Add(id); foreach (var child in children[id]) pending.Push(child.Id); }
        return result;
    }

    /// <summary>Sections precede child groups at each level. Group/section sibling order
    /// is retained. Reveal exposes a selected section without changing saved collapse flags.</summary>
    public static IReadOnlyList<NotebookOutlineEntry> Build(Notebook notebook, string? revealSectionId = null, bool includeCollapsed = false)
    {
        Validate(notebook);
        var selected = notebook.Sections.FirstOrDefault(s => s.Id == revealSectionId);
        var reveal = Ancestors(notebook, selected?.GroupId).ToHashSet(StringComparer.Ordinal);
        var groups = notebook.SectionGroups.ToLookup(g => g.ParentId ?? "", StringComparer.Ordinal);
        var sections = notebook.Sections.ToLookup(s => s.GroupId ?? "", StringComparer.Ordinal);
        var result = new List<NotebookOutlineEntry>();
        void Visit(string parent, int level)
        {
            foreach (var section in sections[parent]) result.Add(new(section.Id, level, null, section));
            foreach (var group in groups[parent])
            {
                result.Add(new(group.Id, level, group, null));
                if (includeCollapsed || !group.IsCollapsed || reveal.Contains(group.Id)) Visit(group.Id, level + 1);
            }
        }
        Visit("", 0); return result;
    }
}
