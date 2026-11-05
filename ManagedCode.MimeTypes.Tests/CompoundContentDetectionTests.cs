using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class CompoundContentDetectionTests
{
    public static IEnumerable<object[]> LegacyFormats()
    {
        (string Extension, string StreamName)[] formats =
        [
            ("doc", "WordDocument"), ("dot", "WordDocument"),
            ("xls", "Workbook"), ("xlt", "Workbook"), ("xla", "Book"),
            ("ppt", "PowerPoint Document"), ("pot", "PowerPoint Document"), ("pps", "PowerPoint Document"),
            ("vsd", "VisioDocument"), ("vss", "VisioDocument"), ("vst", "VisioDocument")
        ];
        foreach (var (extension, streamName) in formats)
        {
            foreach (var version4 in new[] { false, true })
            {
                foreach (var nonSeekable in new[] { false, true })
                {
                    yield return [extension, streamName, version4, nonSeekable];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(LegacyFormats))]
    public void RootDirectoryStream_ShouldDetectOfficeFamily(string extension, string streamName, bool version4, bool nonSeekable)
    {
        using var storage = new MemoryStream(CreateCompound(version4, streamName));
        using Stream stream = nonSeekable ? new OfficeContentDetectionTests.NonSeekableStream(storage) : storage;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.GetMimeType("document." + extension));
        stream.CanRead.ShouldBeTrue();
        if (!nonSeekable) stream.Position.ShouldBe(0);
    }

    [Theory]
    [InlineData("UnrecognizedStream")]
    [InlineData("EncryptedPackage")]
    [InlineData("EncryptionInfo")]
    public void UnknownOrEncryptedCompound_ShouldRetainGenericType(string name)
    {
        using var stream = new MemoryStream(CreateCompound(false, name));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe("application/vnd.ms-office");
    }

    [Fact]
    public void ConflictingRootStreams_ShouldRetainGenericType()
    {
        using var stream = new MemoryStream(CreateCompound(false, "WordDocument", "Workbook"));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe("application/vnd.ms-office");
    }

    [Fact]
    public void EmbeddedOfficeStream_ShouldNotDetermineTheOuterType()
    {
        var bytes = CreateCompound(false, "ObjectPool", "WordDocument");
        bytes[1024 + 128 + 66] = 1;
        SetUInt32(bytes, 1024 + 128 + 72, uint.MaxValue);
        SetUInt32(bytes, 1024 + 128 + 76, 2);
        using var stream = new MemoryStream(bytes);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe("application/vnd.ms-office");
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("sector")]
    [InlineData("byte-order")]
    [InlineData("shift")]
    [InlineData("name-length")]
    [InlineData("entry-cycle")]
    [InlineData("truncated")]
    public void CorruptCompoundDirectory_ShouldRetainGenericType(string corruption)
    {
        var bytes = CreateCompound(false, "WordDocument");
        switch (corruption)
        {
            case "cycle": SetUInt32(bytes, 512 + 4, 1); break;
            case "sector": SetUInt32(bytes, 48, uint.MaxValue - 10); break;
            case "byte-order": bytes[28] = 0; break;
            case "shift": bytes[30] = 31; break;
            case "name-length": bytes[1024 + 128 + 64] = 127; break;
            case "entry-cycle": SetUInt32(bytes, 1024 + 128 + 72, 1); break;
            case "truncated": bytes = bytes[..1100]; break;
        }
        using var stream = new MemoryStream(bytes);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe("application/vnd.ms-office");
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void CompoundProbe_ShouldRestoreNonzeroPosition()
    {
        using var stream = new MemoryStream();
        stream.Write("prefix"u8);
        stream.Write(CreateCompound(false, "Workbook"));
        stream.Position = 6;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.XLS);
        stream.Position.ShouldBe(6);
    }

    private static byte[] CreateCompound(bool version4, params string[] names)
    {
        var size = version4 ? 4096 : 512;
        var bytes = new byte[size * 3];
        Convert.FromHexString("D0CF11E0A1B11AE1").CopyTo(bytes, 0);
        SetUInt16(bytes, 24, 0x003E);
        SetUInt16(bytes, 26, (ushort)(version4 ? 4 : 3));
        SetUInt16(bytes, 28, 0xFFFE);
        SetUInt16(bytes, 30, (ushort)(version4 ? 12 : 9));
        SetUInt16(bytes, 32, 6);
        SetUInt32(bytes, 40, version4 ? 1u : 0);
        SetUInt32(bytes, 44, 1);
        SetUInt32(bytes, 48, 1);
        SetUInt32(bytes, 56, 4096);
        SetUInt32(bytes, 60, 0xFFFFFFFE);
        SetUInt32(bytes, 68, 0xFFFFFFFE);
        for (var index = 0; index < 109; index++) SetUInt32(bytes, 76 + index * 4, uint.MaxValue);
        SetUInt32(bytes, 76, 0);
        for (var offset = size; offset < size * 2; offset += 4) SetUInt32(bytes, offset, uint.MaxValue);
        SetUInt32(bytes, size, 0xFFFFFFFD);
        SetUInt32(bytes, size + 4, 0xFFFFFFFE);
        WriteDirectoryEntry(bytes, size * 2, "Root Entry", 5);
        SetUInt32(bytes, size * 2 + 76, 1);
        for (var index = 0; index < names.Length; index++)
        {
            var offset = size * 2 + (index + 1) * 128;
            WriteDirectoryEntry(bytes, offset, names[index], 2);
            SetUInt32(bytes, offset + 72, index + 1 < names.Length ? (uint)index + 2 : uint.MaxValue);
        }

        return bytes;
    }

    private static void WriteDirectoryEntry(byte[] bytes, int offset, string name, byte type)
    {
        var nameBytes = Encoding.Unicode.GetBytes(name + "\0");
        nameBytes.CopyTo(bytes, offset);
        SetUInt16(bytes, offset + 64, (ushort)nameBytes.Length);
        bytes[offset + 66] = type;
        bytes[offset + 67] = 1;
        SetUInt32(bytes, offset + 68, uint.MaxValue);
        SetUInt32(bytes, offset + 72, uint.MaxValue);
        SetUInt32(bytes, offset + 76, uint.MaxValue);
        SetUInt32(bytes, offset + 116, 0xFFFFFFFE);
    }

    private static void SetUInt16(byte[] bytes, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), value);
    private static void SetUInt32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
