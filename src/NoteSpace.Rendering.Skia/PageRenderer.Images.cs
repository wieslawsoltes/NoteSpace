using NoteSpace.Core;
using SkiaSharp;

namespace NoteSpace.Rendering.Skia;

public sealed partial class PageRenderer
{
    private sealed class CachedImage(byte[] data, SKBitmap? bitmap, long revision, long used)
    {
        public byte[] Data { get; } = data;
        public SKBitmap? Bitmap { get; } = bitmap;
        public long Revision { get; } = revision;
        public long Used { get; set; } = used;
        public long Weight { get; } = data.LongLength + (bitmap?.ByteCount ?? 0);
    }
    private readonly Dictionary<string, CachedImage> images = new();
    private long imageBudget = 64 * 1024 * 1024;
    /// <summary>Retained encoded and decoded image payload budget. Decoder temporaries,
    /// Skia object overhead and GPU copies are not included. Oversized valid images are
    /// drawn transiently. The existing 16-megapixel per-image safety limit still applies.</summary>
    public long MaximumImageCacheBytes
    {
        get => imageBudget;
        set { ObjectDisposedException.ThrowIf(disposed, this); if (value < 1) throw new ArgumentOutOfRangeException(nameof(value)); imageBudget = value; TrimImages(0, false); }
    }
    public long RetainedImageBytes { get; private set; }
    private void RemoveImage(string id)
    {
        if (!images.Remove(id, out var entry)) return;
        entry.Bitmap?.Dispose(); RetainedImageBytes -= entry.Weight;
    }
    private void TrimImages(long incoming, bool adding)
    {
        while (images.Count > 0 && (RetainedImageBytes > imageBudget - incoming || images.Count + (adding ? 1 : 0) > 24))
            RemoveImage(images.MinBy(x => x.Value.Used).Key);
    }
    private void ClearImages()
    {
        foreach (var image in images.Values) image.Bitmap?.Dispose(); images.Clear(); RetainedImageBytes = 0;
    }
    private void DrawImage(SKCanvas canvas, NoteBlock block, long? revision)
    {
        if (block.Data is not { } data) return;
        if (revision.HasValue && images.TryGetValue(block.Id, out var cached) && cached.Revision == revision && ReferenceEquals(cached.Data, data))
        {
            cached.Used = ++useClock; Statistics.ImageCacheHits++; Draw(cached.Bitmap); return;
        }
        RemoveImage(block.Id);
        SKBitmap? bitmap = null;
        Statistics.ImageDecodeAttempts++;
        using (var memory = new SKMemoryStream(data))
        using (var codec = SKCodec.Create(memory))
        {
            if (codec is not null && codec.Info.Width > 0 && codec.Info.Height > 0 && (long)codec.Info.Width * codec.Info.Height <= 16 * 1024 * 1024)
            {
                // Evict before allocating the replacement, not after accumulating
                // another full-size image alongside an already-full cache.
                TrimImages(data.LongLength + (long)codec.Info.Width * codec.Info.Height * 4, true);
                bitmap = SKBitmap.Decode(data);
            }
        }
        var retain = false;
        try
        {
            var weight = data.LongLength + (bitmap?.ByteCount ?? 0);
            if (revision.HasValue && weight <= imageBudget)
            {
                TrimImages(weight, true);
                images.Add(block.Id, new(data, bitmap, revision.Value, ++useClock)); RetainedImageBytes += weight; retain = true;
            }
            Draw(bitmap);
        }
        finally { if (!retain) bitmap?.Dispose(); }
        void Draw(SKBitmap? image)
        {
            if (image is null) { Text(canvas, "Image is invalid or exceeds 16 megapixels", block.X + 12, block.Y + 30, new TextFormat { Color = 0xFFB03030 }); return; }
            var scale = Math.Min(block.Width / image.Width, block.Height / image.Height);
            canvas.DrawBitmap(image, SKRect.Create(block.X, block.Y, image.Width * scale, image.Height * scale));
        }
    }
}
