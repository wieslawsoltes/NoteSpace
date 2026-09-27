using Windows.Storage;
using Windows.Storage.Streams;

namespace NoteSpace.App;

public sealed partial class WorkspaceView
{
    /// <summary>Uses Uno's existing Open Sans package assets; no proprietary font is bundled.</summary>
    public async Task PrepareTypographyAsync()
    {
        if (!OperatingSystem.IsBrowser()) return;
        try
        {
            var styles = new[] { ("Regular", false, false), ("Bold", true, false), ("Italic", false, true), ("BoldItalic", true, true) };
            foreach (var style in styles)
            {
                var file = await StorageFile.GetFileFromApplicationUriAsync(new Uri($"ms-appx:///Uno.Fonts.OpenSans/Fonts/OpenSans-{style.Item1}.ttf"));
                var buffer = await FileIO.ReadBufferAsync(file);
                using var reader = DataReader.FromBuffer(buffer);
                var data = new byte[buffer.Length]; reader.ReadBytes(data);
                // Browser Skia cannot enumerate fonts installed on the host operating system.
                foreach (var family in new[] { "Segoe UI", "Open Sans", "Arial", "Calibri" })
                    surface.Renderer.RegisterTypeface(family, style.Item2, style.Item3, data);
            }
            surface.Refresh();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("NoteSpace font fallback: " + error.Message);
        }
    }
}
