using System;
using System.Buffers.Binary;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private static string? DetectRiffType(ReadOnlySpan<byte> header)
    {
        if (header.Length < 12 || BinaryPrimitives.ReadUInt32LittleEndian(header[4..]) < 4) return null;
        var format = header.Slice(8, 4);
        if (format.SequenceEqual(WebpFourCC)) return WEBP;
        if (format.SequenceEqual(AviFourCC)) return AVI;
        if (format.SequenceEqual(WaveFourCC)) return WAV;
        return null;
    }

    private static string? DetectIsoBaseMediaType(ReadOnlySpan<byte> header)
    {
        while (header.Length >= 8)
        {
            var size = (ulong)BinaryPrimitives.ReadUInt32BigEndian(header);
            var headerSize = 8;
            if (size == 1)
            {
                if (header.Length < 16) return null;
                size = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
                headerSize = 16;
            }

            var type = header.Slice(4, 4);
            if (!type.SequenceEqual(FtypFourCC))
            {
                if ((!type.SequenceEqual("free"u8) && !type.SequenceEqual("skip"u8) && !type.SequenceEqual("wide"u8)) ||
                    size < (ulong)headerSize || size > (ulong)header.Length)
                {
                    return null;
                }

                header = header[(int)size..];
                continue;
            }

            if (header.Length < headerSize + 8 || (size != 0 && size < (ulong)(headerSize + 8))) return null;
            var available = size == 0 || size > (ulong)header.Length ? header.Length : (int)size;
            var major = GetIsoBrandMime(header.Slice(headerSize, 4));
            if (major != null && major != "video/mp4" && major != "image/heif" && major != "image/heif-sequence")
            {
                return major;
            }

            var detected = major;
            for (var offset = headerSize + 8; offset + 4 <= available; offset += 4)
            {
                var compatible = GetIsoBrandMime(header.Slice(offset, 4));
                if (compatible is "image/avif" or "image/heic" or "image/heic-sequence") return compatible;
                detected ??= compatible;
            }

            return detected;
        }

        return null;
    }

    private static string? GetIsoBrandMime(ReadOnlySpan<byte> brand)
    {
        if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8)) return AVIF;
        if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8)) return HEIC;
        if (brand.SequenceEqual("hevc"u8) || brand.SequenceEqual("hevx"u8)) return HEICS;
        if (brand.SequenceEqual("mif1"u8)) return HEIF;
        if (brand.SequenceEqual("msf1"u8)) return HEIFS;
        if (brand.SequenceEqual("M4A "u8) || brand.SequenceEqual("M4B "u8) || brand.SequenceEqual("M4P "u8)) return M4A;
        if (brand.SequenceEqual("M4V "u8) || brand.SequenceEqual("M4VH"u8) || brand.SequenceEqual("M4VP"u8)) return M4V;
        if (brand[..3].SequenceEqual("3gp"u8) || brand[..3].SequenceEqual("3ge"u8) || brand[..3].SequenceEqual("3gg"u8)) return "video/3gpp";
        if (brand[..3].SequenceEqual("3g2"u8)) return "video/3gpp2";
        if (brand.SequenceEqual(QuickTimeBrand)) return MOV;
        return IsMp4Brand(brand) ? MP4 : null;
    }

    private static string? DetectOggType(ReadOnlySpan<byte> header)
    {
        if (header.Length < 4 || !header[..4].SequenceEqual("OggS"u8)) return null;
        var detected = "application/ogg";
        while (header.Length >= 27 && header[..4].SequenceEqual("OggS"u8) && header[4] == 0)
        {
            var segments = header[26];
            if (header.Length < 27 + segments) break;
            var pageLength = 27 + segments;
            var packetLength = 0;
            var packetComplete = false;
            for (var index = 0; index < segments; index++)
            {
                var length = header[27 + index];
                pageLength += length;
                if (!packetComplete)
                {
                    packetLength += length;
                    packetComplete = length < 255;
                }
            }

            if (pageLength > header.Length) break;
            if ((header[5] & 3) == 2 && packetComplete)
            {
                var packet = header.Slice(27 + segments, packetLength);
                if (packet.Length >= 42 && packet[0] == 0x80 && packet[1..].StartsWith("theora"u8)) return OGV;
                if ((packet.Length >= 30 && packet.StartsWith("\u0001vorbis"u8)) ||
                    (packet.Length >= 19 && packet.StartsWith("OpusHead"u8)) ||
                    (packet.Length >= 80 && packet.StartsWith("Speex   "u8)) ||
                    (packet.Length >= 9 && packet.StartsWith("\u007FFLAC"u8)))
                {
                    detected = OGG;
                }
            }

            header = header[pageLength..];
        }

        return detected;
    }
}
