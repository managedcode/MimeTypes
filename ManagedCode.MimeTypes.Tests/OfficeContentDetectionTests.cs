using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class OfficeContentDetectionTests
{
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string WordMainType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";

    public static IEnumerable<object[]> OfficeFormats()
    {
        (string Extension, string MainType, string? RelationshipType)[] formats =
        [
            ("docx", WordMainType, null),
            ("dotx", "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml", null),
            ("docm", "application/vnd.ms-word.document.macroEnabled.main+xml", null),
            ("dotm", "application/vnd.ms-word.template.macroEnabledTemplate.main+xml", null),
            ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml", null),
            ("xltx", "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml", null),
            ("xlsm", "application/vnd.ms-excel.sheet.macroEnabled.main+xml", null),
            ("xltm", "application/vnd.ms-excel.template.macroEnabled.main+xml", null),
            ("xlam", "application/vnd.ms-excel.addin.macroEnabled.main+xml", null),
            ("xlsb", "application/vnd.ms-excel.sheet.binary.macroEnabled.main", null),
            ("pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml", null),
            ("potx", "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml", null),
            ("ppsx", "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml", null),
            ("pptm", "application/vnd.ms-powerpoint.presentation.macroEnabled.main+xml", null),
            ("potm", "application/vnd.ms-powerpoint.template.macroEnabled.main+xml", null),
            ("ppsm", "application/vnd.ms-powerpoint.slideshow.macroEnabled.main+xml", null),
            ("ppam", "application/vnd.ms-powerpoint.addin.macroEnabled.main+xml", null),
            ("sldx", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml", null),
            ("sldm", "application/vnd.ms-powerpoint.slide.macroEnabled.main+xml", null),
            ("thmx", "application/vnd.openxmlformats-officedocument.themeManager+xml", null),
            ("vsdx", "application/vnd.ms-visio.drawing.main+xml", "http://schemas.microsoft.com/visio/2010/relationships/document"),
            ("vsdm", "application/vnd.ms-visio.drawing.macroEnabled.main+xml", "http://schemas.microsoft.com/visio/2010/relationships/document"),
            ("vssx", "application/vnd.ms-visio.stencil.main+xml", "http://schemas.microsoft.com/visio/2010/relationships/document"),
            ("vssm", "application/vnd.ms-visio.stencil.macroEnabled.main+xml", "http://schemas.microsoft.com/visio/2010/relationships/document"),
            ("vstx", "application/vnd.ms-visio.template.main+xml", "http://schemas.microsoft.com/visio/2010/relationships/document"),
            ("vstm", "application/vnd.ms-visio.template.macroEnabled.main+xml", "http://schemas.microsoft.com/visio/2010/relationships/document"),
            ("xps", "application/vnd.ms-package.xps-fixeddocumentsequence+xml", "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation"),
            ("oxps", "application/oxps-fixeddocumentsequence+xml", "http://schemas.openxps.org/oxps/v1.0/fixedrepresentation"),
            ("3mf", "application/vnd.ms-package.3dmanufacturing-3dmodel+xml", "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel")
        ];

        foreach (var (extension, mainType, relationshipType) in formats)
        {
            yield return [extension, mainType, false, relationshipType!];
            yield return [extension, mainType, true, relationshipType!];
        }
    }

    [Theory]
    [MemberData(nameof(OfficeFormats))]
    public void CompressedManifestBeyondHeader_ShouldIdentifyOfficeFormat(string extension, string mainType, bool nonSeekable, string? relationshipType)
    {
        var bytes = CreatePackage(mainType, streamed: true, relationshipType: relationshipType);
        using var storage = new MemoryStream(bytes);
        using Stream stream = nonSeekable ? new NonSeekableStream(storage) : storage;

        MimeHelper.TryGetMimeTypeByContent(stream, out var mime).ShouldBeTrue();
        mime.ShouldBe(MimeHelper.GetMimeType("report." + extension));
        stream.CanRead.ShouldBeTrue();
        if (!nonSeekable)
        {
            stream.Position.ShouldBe(0);
            MimeHelper.MatchesExtensionByContent("report." + extension, stream).ShouldBeTrue();
            MimeHelper.Instance.GetMimeTypeByContent(stream).ShouldBe(mime);
        }
    }

    [Fact]
    public void StoredMetadata_ShouldRetainMacroEnabledSubtype()
    {
        using var stream = new MemoryStream(CreatePackage("application/vnd.ms-word.document.macroEnabled.main+xml", compressedMetadata: false));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.DOCM);
    }

    [Fact]
    public void UnsupportedZipCompression_ShouldRemainZip()
    {
        var bytes = CreatePackage(WordMainType);
        for (var index = 0; index + 12 <= bytes.Length; index++)
        {
            if (bytes.AsSpan(index, 4).SequenceEqual("PK\u0003\u0004"u8)) bytes[index + 8] = 99;
            if (bytes.AsSpan(index, 4).SequenceEqual("PK\u0001\u0002"u8)) bytes[index + 10] = 99;
        }
        using var stream = new MemoryStream(bytes);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void FilePathAndValidation_ShouldUsePackageContentRatherThanFileExtension()
    {
        var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts/office-content-tests"));
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            File.WriteAllBytes(filePath, CreatePackage(WordMainType));

            MimeHelper.GetMimeTypeByContent(filePath).ShouldBe(MimeHelper.DOCX);
            MimeHelper.MatchesMimeTypeByContent(filePath, MimeHelper.DOCX).ShouldBeTrue();
            MimeHelper.MatchesExtensionByContent(filePath).ShouldBeFalse();
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void SeekableZip_ShouldSkipLargePayloadAndRestoreNonzeroPosition()
    {
        var bytes = CreatePackage(WordMainType, padding: 2 * 1024 * 1024);
        using var stream = new MemoryStream();
        stream.Write("prefix"u8);
        stream.Write(bytes);
        stream.Position = 6;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.DOCX);
        stream.Position.ShouldBe(6);
    }

    [Theory]
    [InlineData("word/document.xml")]
    [InlineData("xl/workbook.xml")]
    [InlineData("ppt/presentation.xml")]
    public void OrdinaryZipWithOfficeNames_ShouldRemainZip(string name)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, name, Encoding.UTF8.GetBytes("word/ xl/ ppt/"), CompressionLevel.NoCompression);
        }
        stream.Position = 0;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Theory]
    [InlineData("<Types>")]
    [InlineData("<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/custom/main.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/>")]
    [InlineData("<Types xmlns='wrong'><Override PartName='/custom/main.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>")]
    [InlineData("<!DOCTYPE Types [<!ENTITY type 'application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'>]><Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/custom/main.xml' ContentType='&type;'/></Types>")]
    [InlineData("<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Override PartName='/missing.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>")]
    public void InvalidManifest_ShouldRemainZip(string manifest)
    {
        using var stream = new MemoryStream(CreatePackage(WordMainType, manifest: manifest));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void TruncatedArchive_ShouldRemainZip()
    {
        var bytes = CreatePackage(WordMainType);
        using var stream = new MemoryStream(bytes[..^30]);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void OversizedManifest_ShouldRemainZip()
    {
        var manifest = $"<Types xmlns='{ContentTypesNamespace}'>" + new string(' ', 300_000) + "</Types>";
        using var stream = new MemoryStream(CreatePackage(WordMainType, manifest: manifest));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void StrictOfficeRelationship_ShouldIdentifyTheMainPart()
    {
        using var stream = new MemoryStream(CreatePackage(WordMainType,
            relationshipType: "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument"));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.DOCX);
    }

    [Fact]
    public void DefaultContentType_ShouldIdentifyTheMainPart()
    {
        var manifest = $"<Types xmlns='{ContentTypesNamespace}'><Default Extension='xml' ContentType='{WordMainType}'/></Types>";
        using var stream = new MemoryStream(CreatePackage(WordMainType, manifest: manifest));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.DOCX);
    }

    [Fact]
    public void UnrelatedOfficePart_ShouldNotDetermineThePackageType()
    {
        var manifest = $"<Types xmlns='{ContentTypesNamespace}'><Override PartName='/docProps/padding.bin' ContentType='{WordMainType}'/></Types>";
        using var stream = new MemoryStream(CreatePackage(WordMainType, manifest: manifest));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void DuplicateDefaultContentTypes_ShouldRemainZip()
    {
        var manifest = $"<Types xmlns='{ContentTypesNamespace}'><Default Extension='xml' ContentType='{WordMainType}'/>" +
            "<Default Extension='xml' ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/></Types>";
        using var stream = new MemoryStream(CreatePackage(WordMainType, manifest: manifest));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void UnknownOverride_ShouldTakePrecedenceOverOfficeDefault()
    {
        var manifest = $"<Types xmlns='{ContentTypesNamespace}'><Default Extension='xml' ContentType='{WordMainType}'/>" +
            "<Override PartName='/custom/main.xml' ContentType='application/xml'/></Types>";
        using var stream = new MemoryStream(CreatePackage(WordMainType, manifest: manifest));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<Relationships>")]
    [InlineData("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='https://package.invalid/custom/main.xml'/></Relationships>")]
    [InlineData("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='https://example.com/main.xml' TargetMode='External'/></Relationships>")]
    public void InvalidMainRelationship_ShouldRemainZip(string? relationships)
    {
        using var stream = new MemoryStream();
        stream.Write(CreatePackage(WordMainType));
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("_rels/.rels")!.Delete();
            if (relationships != null)
            {
                WriteEntry(archive, "_rels/.rels", Encoding.UTF8.GetBytes(relationships), CompressionLevel.Optimal);
            }
        }
        stream.Position = 0;

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void ConflictingMainPartTypes_ShouldRemainZip()
    {
        var manifest = $"<Types xmlns='{ContentTypesNamespace}'>" +
            $"<Override PartName='/custom/main.xml' ContentType='{WordMainType}'/>" +
            "<Override PartName='/custom/main.xml' ContentType='application/vnd.ms-word.document.macroEnabled.main+xml'/></Types>";
        using var stream = new MemoryStream(CreatePackage(WordMainType, manifest: manifest));

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
    }

    [Fact]
    public void OversizedCentralDirectory_ShouldRemainZipWithinReadBudget()
    {
        using var storage = new MemoryStream();
        using (var archive = new ZipArchive(storage, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < 2048; index++)
            {
                archive.CreateEntry(index.ToString() + new string('x', 1024));
            }
        }
        storage.Position = 0;
        using var stream = new ReadCountingStream(storage);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
        stream.BytesRead.ShouldBeLessThanOrEqualTo(1024 * 1024 + 560);
        stream.Position.ShouldBe(0);
    }

    [Fact]
    public void NonZipStream_ShouldKeepItsSmallHeaderRead()
    {
        using var storage = new MemoryStream(new byte[2 * 1024 * 1024]);
        storage.Write("%PDF-"u8);
        storage.Position = 0;
        using var stream = new ReadCountingStream(storage);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.PDF);
        stream.BytesRead.ShouldBe(560);
    }

    [Fact]
    public void NonSeekableZip_ShouldBoundBytesConsumed()
    {
        using var storage = new MemoryStream(CreatePackage(WordMainType, padding: 2 * 1024 * 1024));
        using var stream = new NonSeekableStream(storage);

        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(MimeHelper.ZIP);
        storage.Position.ShouldBeLessThanOrEqualTo(1024 * 1024);
    }

    internal static byte[] CreatePackage(string mainType, int padding = 8192, string? manifest = null, bool streamed = false, string? relationshipType = null, bool compressedMetadata = true)
    {
        using var storage = new MemoryStream();
        using var output = new NonSeekableStream(storage);
        using (var archive = new ZipArchive(streamed ? output : storage, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "docProps/padding.bin", new byte[padding], CompressionLevel.NoCompression);
            manifest ??= $"<Types xmlns='{ContentTypesNamespace}'><Override PartName='/custom/main.xml' ContentType='{mainType}'/></Types>";
            WriteEntry(archive, "[Content_Types].xml", Encoding.UTF8.GetBytes(manifest), compressedMetadata ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
            WriteEntry(archive, "custom/main.xml", "<document/>"u8.ToArray(), CompressionLevel.Optimal);
            relationshipType ??= "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
            var relationships = $"<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='{relationshipType}' Target='custom/main.xml'/></Relationships>";
            WriteEntry(archive, "_rels/.rels", Encoding.UTF8.GetBytes(relationships), CompressionLevel.Optimal);
        }

        return storage.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] bytes, CompressionLevel compression)
    {
        using var entry = archive.CreateEntry(name, compression).Open();
        entry.Write(bytes);
    }

    private sealed class ReadCountingStream(Stream inner) : Stream
    {
        public int BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            BytesRead += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 137));
        public override int Read(Span<byte> buffer) => inner.Read(buffer[..Math.Min(buffer.Length, 137)]);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
