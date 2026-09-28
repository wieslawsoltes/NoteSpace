using NoteSpace.Core;

namespace NoteSpace.Storage;

/// <summary>A validated immutable JSON snapshot. Capture on the document's owning
/// thread before asynchronous I/O; later DTO edits cannot alter these saved bytes.</summary>
public sealed class WorkspaceSnapshot
{
    public string Json { get; }
    public long Revision { get; }
    private WorkspaceSnapshot(string json, long revision) { Json = json; Revision = revision; }

    public static WorkspaceSnapshot Capture(Workspace document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DocumentJson.Validate(document);
        var json = DocumentJson.Serialize(document);
        if (json.Length > DocumentJson.MaxJsonLength) throw new InvalidDataException("Notebook exceeds the 32 MiB JSON interchange limit. Nothing was overwritten.");
        return new(json, document.Revision);
    }
    public static WorkspaceSnapshot Parse(string json)
    {
        // Do not expose a constructor accepting unchecked serialized content.
        var document = DocumentJson.Deserialize(json);
        return new(json, document.Revision);
    }
}

/// <summary>Optional fast path preserving the existing IWorkspaceStore contract.
/// Implementations must still enforce cancellation and optimistic write tokens.</summary>
public interface IWorkspaceSnapshotStore : IWorkspaceStore
{
    Task<string> SaveSnapshotAsync(WorkspaceSnapshot snapshot, string? expectedToken, CancellationToken cancellationToken = default);
}
