using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class MediaContentDetectionTests
{
    [Theory]
    [InlineData("isom", "mp4")]
    [InlineData("qt  ", "mov")]
    [InlineData("M4A ", "m4a")]
    [InlineData("M4V ", "m4v")]
    [InlineData("3gp4", "3gp")]
    [InlineData("3gp6", "3gp")]
    [InlineData("3g2a", "3g2")]
    [InlineData("avif", "avif")]
    [InlineData("avis", "avif")]
    [InlineData("heic", "heic")]
    [InlineData("heix", "heic")]
    [InlineData("hevc", "heics")]
    [InlineData("hevx", "heics")]
    [InlineData("mif1", "heif")]
    [InlineData("msf1", "heifs")]
    public void FileTypeBox_ShouldIdentifyTheSpecificMediaFamily(string brand, string extension)
    {
        var bytes = Ftyp(brand);
        Detect(bytes).ShouldBe(MimeHelper.GetMimeType("media." + extension));
        using var stream = new MemoryStream(bytes);
        MimeHelper.MatchesExtensionByContent("media." + extension, stream).ShouldBeTrue();
    }

    [Theory]
    [InlineData("mif1", "avif", "image/avif")]
    [InlineData("mif1", "heic", "image/heic")]
    [InlineData("msf1", "hevc", "image/heic-sequence")]
    [InlineData("zzzz", "isom", "video/mp4")]
    [InlineData("zzzz", "M4A ", "audio/mp4")]
    public void CompatibleBrands_ShouldResolveAnOtherwiseGenericMajorBrand(string major, string compatible, string expected)
    {
        Detect(Ftyp(major, compatible)).ShouldBe(expected);
    }

    [Fact]
    public void ExtendedSizeAndLeadingPadding_ShouldStillDetectAvif()
    {
        var bytes = new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 8);
        "free"u8.CopyTo(bytes.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 1);
        "ftyp"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16), 24);
        "avif"u8.CopyTo(bytes.AsSpan(24));

        Detect(bytes).ShouldBe(MimeHelper.AVIF);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TruncatedFileTypeBox_ShouldNotClaimMp4(int length)
    {
        Detect(Ftyp("isom")[..length]).ShouldNotBe(MimeHelper.MP4);
    }

    [Fact]
    public void InvalidFileTypeBoxSize_ShouldNotClaimMp4()
    {
        var bytes = Ftyp("isom");
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 8);

        Detect(bytes).ShouldNotBe(MimeHelper.MP4);
    }

    [Theory]
    [InlineData("WEBP", "webp")]
    [InlineData("WAVE", "wav")]
    [InlineData("AVI ", "avi")]
    public void RiffFourCC_ShouldMatchTheCatalogAndValidation(string fourCC, string extension)
    {
        var bytes = new byte[16];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
        Encoding.ASCII.GetBytes(fourCC).CopyTo(bytes, 8);
        using var stream = new MemoryStream(bytes);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.GetMimeType("media." + extension));
        MimeHelper.MatchesExtensionByContent("media." + extension, stream).ShouldBeTrue();
    }

    [Theory]
    [InlineData("RIFF0000WEB", "image/webp")]
    [InlineData("RIFF0000WAV", "audio/x-wav")]
    [InlineData("RIFF0000AVI", "video/x-msvideo")]
    [InlineData("RIFF0000????", "image/webp")]
    public void IncompleteOrUnknownFourCC_ShouldNotClaimASubtype(string signature, string unexpected)
    {
        Detect(Encoding.ASCII.GetBytes(signature)).ShouldNotBe(unexpected);
    }

    public static IEnumerable<object[]> OggCodecs()
    {
        yield return ["ogg", Pad("\u0001vorbis"u8.ToArray(), 30)];
        yield return ["oga", Pad("\u0001vorbis"u8.ToArray(), 30)];
        yield return ["opus", Pad("OpusHead"u8.ToArray(), 19)];
        yield return ["ogv", Pad(new byte[] { 0x80 }.Concat("theora"u8.ToArray()).ToArray(), 42)];
        yield return ["ogx", "unknown-codec"u8.ToArray()];
    }

    [Theory]
    [MemberData(nameof(OggCodecs))]
    public void OggIdentificationPacket_ShouldSeparateAudioVideoAndUnknown(string extension, byte[] packet)
    {
        Detect(OggPage(packet)).ShouldBe(MimeHelper.GetMimeType("media." + extension));
    }

    [Fact]
    public void TheoraAfterAnAudioBosPage_ShouldDetectVideo()
    {
        var audio = OggPage(Pad("OpusHead"u8.ToArray(), 19));
        var video = OggPage(Pad(new byte[] { 0x80 }.Concat("theora"u8.ToArray()).ToArray(), 42));

        Detect(audio.Concat(video).ToArray()).ShouldBe(MimeHelper.OGV);
    }

    [Fact]
    public void IncompleteOggCodecPacket_ShouldKeepGenericContainerType()
    {
        var bytes = OggPage(Pad("OpusHead"u8.ToArray(), 19));

        Detect(bytes[..^1]).ShouldBe("application/ogg");
        Detect("OggS"u8.ToArray()).ShouldBe("application/ogg");
    }

    private static byte[] Ftyp(string brand, params string[] compatible)
    {
        var bytes = new byte[16 + compatible.Length * 4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)bytes.Length);
        "ftyp"u8.CopyTo(bytes.AsSpan(4));
        Encoding.ASCII.GetBytes(brand).CopyTo(bytes, 8);
        for (var index = 0; index < compatible.Length; index++) Encoding.ASCII.GetBytes(compatible[index]).CopyTo(bytes, 16 + index * 4);
        return bytes;
    }

    private static byte[] OggPage(byte[] packet)
    {
        var bytes = new byte[28 + packet.Length];
        "OggS"u8.CopyTo(bytes);
        bytes[5] = 2;
        bytes[26] = 1;
        bytes[27] = (byte)packet.Length;
        packet.CopyTo(bytes, 28);
        return bytes;
    }

    private static byte[] Pad(byte[] prefix, int length)
    {
        var bytes = new byte[length];
        prefix.CopyTo(bytes, 0);
        return bytes;
    }

    private static string Detect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var detected = MimeHelper.GetMimeTypeByContent(stream);
        stream.Position.ShouldBe(0);
        return detected;
    }
}
