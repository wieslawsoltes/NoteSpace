using System.Text;
using NoteSpace.Core;
using NoteSpace.Editor;
using NoteSpace.Storage;
using SkiaSharp;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    private async Task ExportAsync(string format)
    {
        surface.FlushPendingText();
        if (surface.HasPendingText) throw new InvalidOperationException("Resolve the text-edit error before exporting, so no draft content is lost.");
        if (format == "export")
        {
            DocumentJson.Validate(session.Document);
            var json = DocumentJson.Serialize(session.Document);
            if (json.Length > DocumentJson.MaxJsonLength) throw new InvalidDataException("This workspace exceeds the interchange limit. Remove oversized attachments or redundant versions before exporting.");
            await platform.SaveFileAsync($"NoteSpace-{DateTime.Now:yyyy-MM-dd}.notespace", Encoding.UTF8.GetBytes(json), "application/json");
            return;
        }
        if (CurrentPage is null) throw new InvalidOperationException("Select a page to export.");
        var name = NoteExport.SafeFileName(CurrentPage.Title); if (string.IsNullOrEmpty(name)) name = "NoteSpace-page";
        if (format == "markdown") await platform.SaveFileAsync(name + ".md", Encoding.UTF8.GetBytes(NoteExport.Markdown(CurrentPage)), "text/markdown;charset=utf-8");
        else if (format == "html") await platform.SaveFileAsync(name + ".html", Encoding.UTF8.GetBytes(NoteExport.Html(CurrentPage)), "text/html;charset=utf-8");
        else if (format == "png") await platform.SaveFileAsync(name + ".png", surface.Renderer.ExportPng(CurrentPage), "image/png");
    }
    private async Task ImportAsync()
    {
        surface.EndEditing();
        var file = await platform.PickFileAsync(".notespace,.json,.md,.txt"); if (file is null) return;
        var extension = Path.GetExtension(file.Name).ToLowerInvariant();
        var text = new UTF8Encoding(false, true).GetString(file.Bytes).TrimStart('\uFEFF');
        if (extension is ".notespace" or ".json")
        {
            var imported = DocumentJson.Deserialize(text);
            if (!await ConfirmAsync("Open this notebook backup?", "Replace the current workspace with the selected .notespace backup? Export your current workspace first to keep a separate copy. You can undo the import in this session.", "Open backup")) return;
            session.Replace(imported); navigationHistory.Clear(); displayedPageId = null; sectionId = null; ApplyTheme(); surface.SetZoom(session.Document.Settings.Zoom); await SaveAsync();
        }
        else if (extension is ".md" or ".txt")
        {
            var page = NoteExport.ImportText(text, EditorSession.CleanTitle(Path.GetFileNameWithoutExtension(file.Name)));
            foreach (var b in page.Blocks) b.Height = Math.Max(b.Height, surface.Renderer.MeasureHeight(b));
            var section = CurrentSection ?? session.AddNotebook("Imported notes").Sections[0];
            session.Execute("Import text page", w => { section.Pages.Add(page); w.Settings.SelectedPageId = page.Id; }, true); Navigate(page.Id);
        }
        else throw new InvalidDataException("Open a .notespace, .json, .md, or .txt file. Microsoft .one and .onepkg files are not supported.");
    }
    private async Task InsertFileAsync(bool image)
    {
        surface.EndEditing(); if (CurrentPage is null) throw new InvalidOperationException("Select or create a page first.");
        var file = await platform.PickFileAsync(image ? "image/png,image/jpeg,image/webp" : ""); if (file is null) return;
        if (file.Bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Attachments are limited to 16 MiB each.");
        var mediaType = file.MediaType;
        var block = new NoteBlock { Kind = image ? BlockKind.Image : BlockKind.Attachment, FileName = file.Name, MediaType = mediaType, Data = file.Bytes, X = 48, Y = NextY(), Width = 420, Height = 72 };
        if (image)
        {
            using var stream = new SKMemoryStream(file.Bytes); using var codec = SKCodec.Create(stream);
            if (codec is null) throw new InvalidDataException("The selected file is not a supported raster image.");
            if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || (long)codec.Info.Width * codec.Info.Height > 16 * 1024 * 1024) throw new InvalidDataException("Images are limited to 16 megapixels.");
            block.MediaType = codec.EncodedFormat switch { SKEncodedImageFormat.Png => "image/png", SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Webp => "image/webp", _ => throw new InvalidDataException("Use a PNG, JPEG, or WebP image.") };
            var scale = Math.Min(1, 720f / codec.Info.Width); block.Width = Math.Max(24, codec.Info.Width * scale); block.Height = Math.Max(12, codec.Info.Height * scale);
        }
        else if (string.IsNullOrWhiteSpace(block.MediaType)) block.MediaType = "application/octet-stream";
        surface.InsertBlock(block);
    }
}
