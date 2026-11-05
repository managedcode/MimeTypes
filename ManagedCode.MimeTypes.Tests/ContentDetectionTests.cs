using System;
using System.IO;
using System.Linq;
using System.Text;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class ContentDetectionTests
{
    [Fact]
    public void PdfHeader_ShouldBeDetected()
    {
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-1.7\n");
        Detect(pdfBytes).ShouldBe("application/pdf");
    }

    [Fact]
    public void IncompletePdfHeader_ShouldNotBeDetected()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDFx");

        Detect(bytes).ShouldBe(MimeHelper.BIN);
    }

    [Fact]
    public void MetadataMagicSignature_ShouldBeDetected()
    {
        var fdfBytes = Encoding.ASCII.GetBytes("%FDF-1.2\n");

        Detect(fdfBytes).ShouldBe("application/fdf");
    }

    [Fact]
    public void SharedRegistryMagicSignature_ShouldNotChooseAnArbitraryMime()
    {
        // AC-3 and E-AC-3 share this signature.
        Detect([0x0B, 0x77]).ShouldBe(MimeHelper.BIN);
    }

    [Fact]
    public void PngHeader_ShouldBeDetected()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };
        Detect(pngBytes).ShouldBe("image/png");
    }

    [Fact]
    public void WebpHeader_ShouldBeDetected()
    {
        var webpBytes = Combine(
            new byte[] { 0x52, 0x49, 0x46, 0x46, 0x2A, 0x00, 0x00, 0x00 },
            Encoding.ASCII.GetBytes("WEBP"),
            new byte[] { 0x56, 0x50, 0x38, 0x4C });
        Detect(webpBytes).ShouldBe("image/webp");
    }

    [Theory]
    [InlineData("GIF87a", true)]
    [InlineData("GIF89a", true)]
    [InlineData("GIF8", false)]
    [InlineData("GIF88a", false)]
    public void GifProbe_ShouldRequireTheCompleteVersionedHeader(string header, bool expectedMatch)
    {
        (Detect(Encoding.ASCII.GetBytes(header)) == MimeHelper.GIF).ShouldBe(expectedMatch);
    }

    [Theory]
    [InlineData("526172211A0700", true)]
    [InlineData("526172211A070100", true)]
    [InlineData("52617221", false)]
    [InlineData("526172211A0701", false)]
    public void RarProbe_ShouldRequireTheCompleteVersionedHeader(string header, bool expectedMatch)
    {
        (Detect(Convert.FromHexString(header)) == "application/x-rar-compressed").ShouldBe(expectedMatch);
    }

    [Fact]
    public void Mp4Header_ShouldBeDetected()
    {
        var mp4Bytes = Combine(
            new byte[] { 0x00, 0x00, 0x00, 0x18 },
            Encoding.ASCII.GetBytes("ftyp"),
            Encoding.ASCII.GetBytes("isom"),
            Encoding.ASCII.GetBytes("isom"));
        Detect(mp4Bytes).ShouldBe("video/mp4");
    }

    [Theory]
    [InlineData(0xFB, 0x90, true)]
    [InlineData(0xE3, 0x80, true)]
    [InlineData(0xFB, 0x00, true)]
    [InlineData(0xEB, 0x90, false)]
    [InlineData(0xF1, 0x50, false)]
    [InlineData(0xFB, 0xF0, false)]
    [InlineData(0xFB, 0x9C, false)]
    public void MpegAudioSyncWord_ShouldRequireValidFrameFields(int versionAndLayer, int rates, bool expectedMatch)
    {
        var mime = Detect([0xFF, (byte)versionAndLayer, (byte)rates, 0]);
        (mime == MimeHelper.MP3).ShouldBe(expectedMatch);
    }

    [Fact]
    public void ZipHeader_ShouldFallbackToZip()
    {
        var zipBytes = Combine(
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            Encoding.ASCII.GetBytes("hello.txt"));
        Detect(zipBytes).ShouldBe("application/zip");
    }

    [Fact]
    public void OfficePathWithoutPackageMetadata_ShouldRemainZip()
    {
        var docxBytes = Combine(
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            Encoding.ASCII.GetBytes("word/document.xml"));
        Detect(docxBytes).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void OfficePathAtTheEndOfTheProbe_ShouldNotEstablishOfficeType()
    {
        var header = new byte[560];
        new byte[] { 0x50, 0x4B, 0x03, 0x04 }.CopyTo(header, 0);
        "WoRd/"u8.CopyTo(header.AsSpan(header.Length - 5));

        Detect(header).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void ShortStream_ShouldReturnDefault()
    {
        using var stream = new MemoryStream(new byte[] { 0x01, 0x02 });
        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.BIN);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void EmptyStream_ShouldReturnDefault()
    {
        using var stream = new MemoryStream();
        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.BIN);
    }

    [Fact]
    public void TryGetMimeTypeByContent_ShouldReturnFalseWhenNoSignatureMatches()
    {
        using var stream = new MemoryStream(new byte[] { 0x01, 0x02 });

        MimeHelper.TryGetMimeTypeByContent(stream, out var mime).ShouldBeFalse();
        mime.ShouldBe(MimeHelper.BIN);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void TryGetMimeTypeByContent_ShouldReturnTrueWhenSignatureMatches()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-2.0\n"));

        MimeHelper.TryGetMimeTypeByContent(stream, out var mime).ShouldBeTrue();
        mime.ShouldBe(MimeHelper.PDF);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void SeekableStream_ShouldRestoreTheOriginalPositionOnce()
    {
        using var stream = new SeekCountingStream("xx%PDF-1.7"u8.ToArray());
        stream.Position = 2;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.PDF);

        stream.Position.ShouldBe(2);
        stream.SeekCount.ShouldBe(1);
    }

    [Fact]
    public void FilePathOverload_ShouldDetect()
    {
        var pdfBytes = Encoding.ASCII.GetBytes("%PDF-2.0\n");
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, pdfBytes);
            MimeHelper.GetMimeTypeByContent(tempFile).ShouldBe(MimeHelper.PDF);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void MatchesMimeTypeByContent_ShouldValidateExpectedMime()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-2.0\n"));

        MimeHelper.MatchesMimeTypeByContent(stream, MimeHelper.PDF).ShouldBeTrue();
        stream.Position.ShouldBe(0);

        MimeHelper.MatchesMimeTypeByContent(stream, MimeHelper.PNG).ShouldBeFalse();
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void MatchesExtensionByContent_ShouldValidateFileNameAgainstStream()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-2.0\n"));

        MimeHelper.MatchesExtensionByContent("report.pdf", stream).ShouldBeTrue();
        stream.Position.ShouldBe(0);

        MimeHelper.MatchesExtensionByContent("report.png", stream).ShouldBeFalse();
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void MatchesExtensionByContent_ShouldValidateFilePathAgainstContent()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            File.WriteAllBytes(tempFile, Encoding.ASCII.GetBytes("%PDF-2.0\n"));

            MimeHelper.MatchesExtensionByContent(tempFile).ShouldBeTrue();

            File.WriteAllBytes(tempFile, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            MimeHelper.MatchesExtensionByContent(tempFile).ShouldBeFalse();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private sealed class SeekCountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int SeekCount { get; private set; }

        public override long Seek(long offset, SeekOrigin loc)
        {
            SeekCount++;
            return base.Seek(offset, loc);
        }
    }

    private static string Detect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var detected = MimeHelper.GetMimeTypeByContent(stream);
        stream.Position.ShouldBe(0);
        return detected;
    }

    private static byte[] Combine(params byte[][] segments)
    {
        var totalLength = segments.Sum(static s => s.Length);
        var buffer = new byte[totalLength];
        var offset = 0;
        foreach (var segment in segments)
        {
            Buffer.BlockCopy(segment, 0, buffer, offset, segment.Length);
            offset += segment.Length;
        }

        return buffer;
    }
}
