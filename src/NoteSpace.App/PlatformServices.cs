using NoteSpace.Core;
using NoteSpace.Storage;
#if BROWSER_WASM
using System.Runtime.InteropServices.JavaScript;
#endif

namespace NoteSpace.App;

public sealed record PickedNoteFile(string Name, string MediaType, byte[] Bytes);

public sealed class PlatformServices
{
    public IWorkspaceStore Store { get; }
    public PlatformServices()
    {
#if BROWSER_WASM
        Store = new BrowserWorkspaceStore();
#else
        Store = new FileWorkspaceStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteSpace", "workspace.notespace"));
#endif
    }
    public async Task<PickedNoteFile?> PickFileAsync(string accept = "")
    {
#if BROWSER_WASM
        var result = await BrowserBridge.Pick(accept);
        if (string.IsNullOrEmpty(result)) return null;
        var parts = result.Split('\n', 3);
        if (parts.Length != 3) throw new InvalidDataException("Could not read the selected file.");
        return new PickedNoteFile(parts[0], parts[1], Convert.FromBase64String(parts[2]));
#else
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        var file = await picker.PickSingleFileAsync(); if (file is null) return null;
        var properties = await file.GetBasicPropertiesAsync();
        if (properties.Size > 32 * 1024 * 1024) throw new InvalidDataException("The selected file exceeds 32 MiB.");
        var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
        using var reader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
        var bytes = new byte[buffer.Length]; reader.ReadBytes(bytes);
        return new PickedNoteFile(file.Name, file.ContentType, bytes);
#endif
    }
    public async Task SaveFileAsync(string name, byte[] bytes, string type)
    {
#if BROWSER_WASM
        BrowserBridge.Download(name, Convert.ToBase64String(bytes), type);
        await Task.CompletedTask;
#else
        var picker = new Windows.Storage.Pickers.FileSavePicker { SuggestedFileName = name };
        var extension = Path.GetExtension(name); if (string.IsNullOrEmpty(extension)) extension = ".bin";
        picker.FileTypeChoices.Add("NoteSpace export", new List<string> { extension });
        var file = await picker.PickSaveFileAsync();
        if (file is not null) await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
#endif
    }
    public void BindFormatShortcuts(Func<bool> copy, Func<bool> paste)
    {
        ArgumentNullException.ThrowIfNull(copy); ArgumentNullException.ThrowIfNull(paste);
#if BROWSER_WASM
        BrowserBridge.BindFormatShortcuts(copy, paste);
#endif
    }
    public void ClearFormatShortcuts()
    {
#if BROWSER_WASM
        BrowserBridge.ClearFormatShortcuts();
#endif
    }
    public void Report(string json)
    {
#if BROWSER_WASM
        BrowserBridge.Report(json);
#endif
    }
}

#if BROWSER_WASM
internal static partial class BrowserBridge
{
    [JSImport("globalThis.NoteSpaceHost.bindFormatShortcuts")]
    internal static partial void BindFormatShortcuts(
        [JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> copy,
        [JSMarshalAs<JSType.Function<JSType.Boolean>>] Func<bool> paste);
    [JSImport("globalThis.NoteSpaceHost.clearFormatShortcuts")]
    internal static partial void ClearFormatShortcuts();
    [JSImport("globalThis.NoteSpaceHost.load")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Load();
    [JSImport("globalThis.NoteSpaceHost.save")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Save(string json, string token);
    [JSImport("globalThis.NoteSpaceHost.pick")]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    internal static partial Task<string> Pick(string accept);
    [JSImport("globalThis.NoteSpaceHost.download")]
    internal static partial void Download(string name, string data, string type);
    [JSImport("globalThis.NoteSpaceHost.report")]
    internal static partial void Report(string json);
}
internal sealed class BrowserWorkspaceStore : IWorkspaceSnapshotStore
{
    public async Task<StoredWorkspace?> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await BrowserBridge.Load(); if (result.Length == 0) return null;
        var separator = result.IndexOf('\n');
        if (separator < 1) throw new InvalidDataException("Invalid stored notebook header. Export a backup before resetting storage.");
        return new StoredWorkspace(DocumentJson.Deserialize(result[(separator + 1)..]), result[..separator]);
    }
    public Task<string> SaveAsync(Workspace document, string? expectedToken, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SaveSnapshotAsync(WorkspaceSnapshot.Capture(document), expectedToken, cancellationToken);
    }
    public async Task<string> SaveSnapshotAsync(WorkspaceSnapshot snapshot, string? expectedToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot); cancellationToken.ThrowIfCancellationRequested();
        try { return await BrowserBridge.Save(snapshot.Json, expectedToken ?? ""); }
        catch (JSException e) when (e.Message.Contains("NOTESPACE_CONFLICT", StringComparison.Ordinal)) { throw new StorageConflictException(); }
    }
}
#endif
