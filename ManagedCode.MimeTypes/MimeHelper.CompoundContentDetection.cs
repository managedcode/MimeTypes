using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private const uint CompoundEndOfChain = 0xFFFFFFFE;
    private const uint CompoundNoStream = 0xFFFFFFFF;

    private static string? ReadCompoundOfficeType(Stream stream)
    {
        var header = new byte[512];
        stream.Seek(0, SeekOrigin.Begin);
        if (ReadUpTo(stream, header, header.Length) != header.Length || ReadUInt16(header, 28) != 0xFFFE)
        {
            return null;
        }

        var version = ReadUInt16(header, 26);
        var shift = ReadUInt16(header, 30);
        if (!((version == 3 && shift == 9) || (version == 4 && shift == 12)))
        {
            return null;
        }

        var sectorSize = 1 << shift;
        var sector = new byte[sectorSize];
        var fat = new byte[sectorSize];
        var visitedSectors = new HashSet<uint>();
        var next = ReadUInt32(header, 48);
        using var directory = new MemoryStream();
        while (next != CompoundEndOfChain)
        {
            if (visitedSectors.Count >= 128 || !visitedSectors.Add(next) ||
                !ReadCompoundSector(stream, next, sectorSize, sector))
            {
                return null;
            }

            directory.Write(sector);
            var fatIndex = next / (uint)(sectorSize / 4);
            // The header's DIFAT covers the normal Office directory range.
            // Larger/unsupported layouts retain the generic compound signature.
            if (fatIndex >= 109 || fatIndex >= ReadUInt32(header, 44) ||
                !ReadCompoundSector(stream, ReadUInt32(header, 76 + (int)fatIndex * 4), sectorSize, fat))
            {
                return null;
            }

            next = ReadUInt32(fat, (int)(next % (uint)(sectorSize / 4)) * 4);
        }

        return DetectCompoundRootStreams(directory.ToArray());
    }

    private static bool ReadCompoundSector(Stream stream, uint sector, int size, byte[] buffer)
    {
        var offset = ((long)sector + 1) * size;
        if (sector >= 0xFFFFFFFA || offset > stream.Length - size)
        {
            return false;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        return ReadUpTo(stream, buffer, size) == size;
    }

    private static string? DetectCompoundRootStreams(byte[] directory)
    {
        if (directory.Length < 128 || directory[66] != 5)
        {
            return null;
        }

        var pending = new Stack<uint>();
        var visited = new HashSet<uint>();
        pending.Push(ReadUInt32(directory, 76));
        string? detected = null;
        while (pending.Count > 0)
        {
            var id = pending.Pop();
            if (id == CompoundNoStream)
            {
                continue;
            }

            if (id == 0 || id >= directory.Length / 128 || !visited.Add(id))
            {
                return null;
            }

            var entry = directory.AsSpan((int)id * 128, 128);
            var nameLength = ReadUInt16(entry, 64);
            if (entry[66] is not (1 or 2) || nameLength is < 2 or > 64 || nameLength % 2 != 0 ||
                ReadUInt16(entry, nameLength - 2) != 0)
            {
                return null;
            }

            pending.Push(ReadUInt32(entry, 68));
            pending.Push(ReadUInt32(entry, 72));
            if (entry[66] != 2)
            {
                // Nested objects may contain embedded Office documents of another type.
                continue;
            }

            var name = Encoding.Unicode.GetString(entry[..(nameLength - 2)]);
            if (name.Equals("EncryptedPackage", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("EncryptionInfo", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var mime = name.ToUpperInvariant() switch
            {
                "WORDDOCUMENT" => DOC,
                "WORKBOOK" or "BOOK" => XLS,
                "POWERPOINT DOCUMENT" => PPT,
                "VISIODOCUMENT" => VSD,
                _ => null
            };
            if (mime != null && detected != null && detected != mime)
            {
                return null;
            }

            detected = mime ?? detected;
        }

        return detected;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, 2));
    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
}
