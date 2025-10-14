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

    [Fact]
    public void ZipHeader_ShouldFallbackToZip()
    {
        var zipBytes = Combine(
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            Encoding.ASCII.GetBytes("hello.txt"));
        Detect(zipBytes).ShouldBe("application/zip");
    }

    [Fact]
    public void DocxHeader_ShouldBeDetected()
    {
        var docxBytes = Combine(
            new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            Encoding.ASCII.GetBytes("word/document.xml"));
        Detect(docxBytes).ShouldBe(MimeHelper.DOCX);
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
