using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class ZipPackageContentDetectionTests
{
    public static IEnumerable<object[]> DeclaredMimeFormats()
    {
        string[] extensions =
        [
            "epub", "odt", "ott", "oth", "odm", "otm", "odg", "otg", "odp", "otp", "ods", "ots",
            "odc", "otc", "odi", "oti", "odf", "odft", "odb",
            "sxw", "stw", "sxc", "stc", "sxi", "sti", "sxd", "std", "sxm", "sxg"
        ];
        foreach (var extension in extensions)
        {
            foreach (var compressed in new[] { false, true })
            {
                foreach (var nonSeekable in new[] { false, true })
                {
                    yield return [extension, compressed, nonSeekable];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(DeclaredMimeFormats))]
    public void MimetypeEntry_ShouldPreserveTheExactSubtype(string extension, bool compressed, bool nonSeekable)
    {
        var expected = MimeHelper.GetMimeType("document." + extension);
        expected.ShouldNotBe(MimeHelper.BIN);
        var bytes = CreateZip(("padding.bin", new byte[8192], false), ("mimetype", Encoding.ASCII.GetBytes(expected), compressed));
        using var storage = new MemoryStream(bytes);
        using Stream stream = nonSeekable ? new OfficeContentDetectionTests.NonSeekableStream(storage) : storage;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(expected);
        stream.CanRead.ShouldBeTrue();
        if (!nonSeekable) stream.Position.ShouldBe(0);
    }

    [Theory]
    [InlineData("mimetypeapplication/epub+zip")]
    [InlineData("mimetypeapplication/vnd.oasis.opendocument.text")]
    [InlineData("AndroidManifest.xml")]
    [InlineData("word/ xl/ ppt/")]
    public void MarkerTextInsideAnOrdinaryEntry_ShouldRemainZip(string marker)
    {
        using var stream = new MemoryStream(CreateZip(("notes.txt", Encoding.ASCII.GetBytes(marker), false)));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.oasis.opendocument.text-unknown")]
    [InlineData("application/epub+zip-malicious")]
    [InlineData("application/epub+zip garbage")]
    public void UnknownOrNonExactMimetype_ShouldRemainZip(string mime)
    {
        using var stream = new MemoryStream(CreateZip(("mimetype", Encoding.ASCII.GetBytes(mime), false)));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AndroidManifestBeyondHeader_ShouldDetectApk(bool nonSeekable)
    {
        var bytes = CreateZip(("padding.bin", new byte[8192], false), ("AndroidManifest.xml", new byte[] { 3, 0, 8, 0, 8, 0, 0, 0 }, true));
        using var storage = new MemoryStream(bytes);
        using Stream stream = nonSeekable ? new OfficeContentDetectionTests.NonSeekableStream(storage) : storage;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.APK);
    }

    [Fact]
    public void MimetypeWithProducerLineEnding_ShouldIdentifyEpub()
    {
        using var stream = new MemoryStream(CreateZip(("mimetype", "application/epub+zip\r\n"u8.ToArray(), true)));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.EPUB);
    }

    [Fact]
    public void FakeAndroidManifestEntry_ShouldRemainZip()
    {
        using var stream = new MemoryStream(CreateZip(("AndroidManifest.xml", "not an Android binary manifest"u8.ToArray(), false)));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Theory]
    [InlineData("jar")]
    [InlineData("war")]
    [InlineData("ear")]
    public void JavaManifest_ShouldDetectJavaArchiveFamily(string extension)
    {
        using var stream = new MemoryStream(CreateZip(("padding.bin", new byte[8192], false),
            ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0\r\nCreated-By: regression\r\n\r\n"u8.ToArray(), true)));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.GetMimeType("archive." + extension));
    }

    [Fact]
    public void DuplicateMimetypeEntries_ShouldRemainZip()
    {
        using var stream = new MemoryStream(CreateZip(("mimetype", "application/epub+zip"u8.ToArray(), false),
            ("mimetype", "application/vnd.oasis.opendocument.text"u8.ToArray(), false)));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void ConflictingPackageFamilies_ShouldRemainZip()
    {
        using var stream = new MemoryStream();
        stream.Write(OfficeContentDetectionTests.CreatePackage("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"));
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            using var content = archive.CreateEntry("mimetype").Open();
            content.Write("application/epub+zip"u8);
        }
        stream.Position = 0;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void OversizedMimetype_ShouldRemainZip()
    {
        using var stream = new MemoryStream(CreateZip(("mimetype", Enumerable.Repeat((byte)'x', 300_000).ToArray(), true)));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    internal static byte[] CreateZip(params (string Name, byte[] Bytes, bool Compressed)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes, compressed) in entries)
            {
                using var content = archive.CreateEntry(name, compressed ? CompressionLevel.Optimal : CompressionLevel.NoCompression).Open();
                content.Write(bytes);
            }
        }

        return stream.ToArray();
    }
}
