using NoteSpace.Core;

namespace NoteSpace.Storage;

public sealed record StoredWorkspace(Workspace Document, string Token);
public sealed class StorageConflictException : IOException
{
    public StorageConflictException() : base("This notebook changed in another window. Export your changes, then reload before saving.") { }
}
public interface IWorkspaceStore
{
    Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default);
    Task<string> SaveAsync(Workspace document, string? expectedToken, CancellationToken cancellationToken = default);
}
/// <summary>Atomic replace plus an exclusive interprocess lock for desktop/CLI consumers.</summary>
public sealed class FileWorkspaceStore(string path) : IWorkspaceSnapshotStore
{
    private readonly string fullPath = Path.GetFullPath(path);
    public async Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(fullPath)) return null;
        // UTF-8 can use multiple bytes per character. Bound the allocation before decoding.
        if (new FileInfo(fullPath).Length > DocumentJson.MaxJsonLength * 4L) throw new InvalidDataException("Stored notebook exceeds the supported size.");
        var json = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return new StoredWorkspace(DocumentJson.Deserialize(json), Token(json));
    }
    public Task<string> SaveAsync(Workspace document, string? expectedToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SaveSnapshotAsync(WorkspaceSnapshot.Capture(document), expectedToken, cancellationToken);
    }
    public async Task<string> SaveSnapshotAsync(WorkspaceSnapshot snapshot, string? expectedToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); cancellationToken.ThrowIfCancellationRequested();
        var json = snapshot.Json;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fileLock = new FileStream(fullPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var existing = await LoadAsync(cancellationToken);
        if (existing?.Token != expectedToken) throw new StorageConflictException();
        var temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, fullPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return Token(json);
    }
    private static string Token(string json) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
}
