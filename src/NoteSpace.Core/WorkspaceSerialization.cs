using System.Buffers;
using System.Text;
using System.Text.Json;

namespace NoteSpace.Core;

/// <summary>Size verification using the same generated JSON writer contract as
/// backups, without materializing another complete workspace string.</summary>
public static class WorkspaceSerialization
{
    public static int MeasureCharacters(Workspace document, int maximumCharacters = DocumentJson.MaxJsonLength)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCharacters);
        var options = NoteJsonContext.Default.Options;
        using var counter = new CharacterCountingWriter(maximumCharacters);
        using var writer = new Utf8JsonWriter(counter, new JsonWriterOptions {
            Encoder = options.Encoder, Indented = options.WriteIndented,
            MaxDepth = options.MaxDepth == 0 ? 64 : options.MaxDepth
        });
        // Use the writer overload (the same generated fast-path contract as string
        // backups), not the Stream serializer's separate continuation path.
        JsonSerializer.Serialize(writer, document, NoteJsonContext.Default.Workspace);
        writer.Flush();
        return counter.Characters;
    }

    private sealed class CharacterCountingWriter(int maximum) : IBufferWriter<byte>, IDisposable
    {
        private byte[]? buffer;
        private long characters;
        public int Characters => (int)characters;
        public void Advance(int count)
        {
            if (buffer is null || (uint)count > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(count));
            var bytes = buffer.AsSpan(0, count);
            // Utf8JsonWriter produces valid UTF-8 only. Count leading bytes;
            // supplementary scalars occupy two UTF-16 characters. Continuations
            // contribute zero, including when a buffer ends inside a scalar.
            if (Ascii.IsValid(bytes)) characters += count;
            else foreach (var value in bytes)
            {
                if ((value & 0xC0) != 0x80) characters++;
                if (value >= 0xF0) characters++;
            }
            if (characters > maximum) throw new InvalidDataException("The edit would exceed the notebook JSON character limit. The previous document has been retained.");
        }
        public Memory<byte> GetMemory(int sizeHint = 0) => EnsureBuffer(sizeHint);
        public Span<byte> GetSpan(int sizeHint = 0) => EnsureBuffer(sizeHint);
        private byte[] EnsureBuffer(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            sizeHint = Math.Max(256, sizeHint);
            if (buffer is null || buffer.Length < sizeHint)
            {
                if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                buffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            }
            return buffer;
        }
        public void Dispose()
        {
            if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            buffer = null;
        }
    }
}
