using System;
using System.Buffers;
using System.IO;
using System.Xml;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private const int MaxContainerProbeBytes = 1024 * 1024;

    private static string? DetectContainerType(Stream stream, long? position, ReadOnlySpan<byte> header, Func<Stream, string?> detect)
    {
        try
        {
            if (position.HasValue)
            {
                // Container offsets are relative to the start of the probed content.
                using var probe = new ContentProbeStream(stream, position.Value);
                return detect(probe);
            }

            // ZipArchive otherwise copies a non-seekable stream without a limit.
            // Replay the header and buffer only a bounded prefix ourselves.
            if (header.Length > MaxContainerProbeBytes)
            {
                return null;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(MaxContainerProbeBytes);
            try
            {
                header.CopyTo(buffer);
                var length = header.Length + ReadUpTo(stream, buffer, MaxContainerProbeBytes - header.Length, header.Length);
                using var buffered = new MemoryStream(buffer, 0, length, writable: false);
                return detect(buffered);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (XmlException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // Unsupported container features cannot establish a specific type.
            return null;
        }
    }

    // Limit container reads while allowing random access past large payloads.
    // This view leaves the caller's stream open; the outer probe restores Position.
    private sealed class ContentProbeStream(Stream inner, long origin) : Stream
    {
        private int _remaining = MaxContainerProbeBytes;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length - origin;
        public override long Position { get => inner.Position - origin; set => Seek(value, SeekOrigin.Begin); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            if (_remaining == 0)
            {
                throw new InvalidDataException("Container content detection read limit exceeded.");
            }

            var read = inner.Read(buffer[..Math.Min(buffer.Length, _remaining)]);
            _remaining -= read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin seekOrigin)
        {
            var target = seekOrigin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => Position + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(seekOrigin))
            };
            if (target < 0 || target > Length)
            {
                throw new InvalidDataException("Container offset is outside the probed content.");
            }

            return inner.Seek(origin + target, SeekOrigin.Begin) - origin;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
