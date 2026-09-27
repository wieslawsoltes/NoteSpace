using System.Text;
using System.Text.Json;

namespace NoteSpace.Core;

/// <summary>Size verification using the same generated JSON contract as backups,
/// without materializing another complete workspace string.</summary>
public static class WorkspaceSerialization
{
    public static int MeasureCharacters(Workspace document, int maximumCharacters = DocumentJson.MaxJsonLength)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCharacters);
        using var counter = new CharacterCountingStream(maximumCharacters);
        JsonSerializer.Serialize(counter, document, NoteJsonContext.Default.Workspace);
        return counter.Complete();
    }

    private sealed class CharacterCountingStream(int maximum) : Stream
    {
        // Input comes only from JsonSerializer and is valid UTF-8. Count leading
        // bytes (four-byte scalar values occupy two UTF-16 characters); continuation
        // bytes contribute zero, even if a write splits the scalar. The normal
        // escaped-ASCII JSON path uses the runtime's vectorized ASCII check.
        private long characters;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Ascii.IsValid(buffer)) { Count(buffer.Length); return; }
            var count = 0;
            foreach (var value in buffer)
            {
                if ((value & 0xC0) != 0x80) count++;
                if (value >= 0xF0) count++;
            }
            Count(count);
        }
        public int Complete() => (int)characters;
        private void Count(int count)
        {
            characters += count;
            if (characters > maximum) throw new InvalidDataException("The edit would exceed the notebook JSON character limit. The previous document has been retained.");
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
