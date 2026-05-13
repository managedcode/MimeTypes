using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class GeneratorTests
{
    [Fact]
    public void ExtensionsTest()
    {
        MimeHelper.GetMimeType("somefile.pdf").ShouldBe(MimeHelper.PDF);
        MimeHelper.GetMimeType("pdf").ShouldBe(MimeHelper.PDF);
        MimeHelper.GetMimeType(".gz").ShouldBe(MimeHelper.GZ);
        MimeHelper.GetMimeType("word.docx").ShouldBe(MimeHelper.DOCX);
        MimeHelper.GetMimeType("C:\\\\users\\file.txt").ShouldBe(MimeHelper.TXT);
        MimeHelper.GetMimeType("https://cdn.example.com/assets/image.png?version=1").ShouldBe(MimeHelper.PNG);
        MimeHelper.GetMimeType("ARCHIVE.TAR.GZ").ShouldBe(MimeHelper.GZ);
        MimeHelper.GetMimeType("module.d.ts").ShouldBe(MimeHelper.D_TS);
        MimeHelper.GetMimeType("events.event_stream").ShouldBe(MimeHelper.EVENT_STREAM);
    }

    [Fact]
    public void EmptyExtensionsTest()
    {
        MimeHelper.GetMimeType("").ShouldBe(MimeHelper.BIN);
        MimeHelper.GetMimeType("     ").ShouldBe(MimeHelper.BIN);
        MimeHelper.GetMimeType(null as string).ShouldBe(MimeHelper.BIN);
    }
    
    [Fact]
    public void GeneratedPropertiesTest()
    {
        // Test static properties generated from mimeTypes.json
        const string eventStreamMime = "text/event-stream";
        MimeHelper.PDF.ShouldBe(MimeHelper.GetMimeType(".pdf"));
        MimeHelper.DOCX.ShouldBe(MimeHelper.GetMimeType(".docx"));
        MimeHelper.PNG.ShouldBe(MimeHelper.GetMimeType(".png"));
        MimeHelper.MP4.ShouldBe(MimeHelper.GetMimeType(".mp4"));
        MimeHelper._7Z.ShouldBe(MimeHelper.GetMimeType(".7z"));
        MimeHelper.EVENT_STREAM.ShouldBe(eventStreamMime);
    }

    [Fact]
    public void GeneratedDictionaryTest()
    {
        // Test if dictionary is properly initialized
        MimeHelper.GetMimeType(".pdf").ShouldBe(MimeHelper.PDF);
        MimeHelper.GetMimeType(".docx").ShouldBe(MimeHelper.DOCX);
        MimeHelper.GetMimeType(".7z").ShouldBe(MimeHelper._7Z);
    }

    [Theory]
    [InlineData("index.html", "text/html")]
    [InlineData("data.json", "application/json")]
    [InlineData("feed.xml", "application/xml")]
    [InlineData("clip.mp4", "video/mp4")]
    [InlineData("song.mid", "audio/midi")]
    [InlineData("audio.aac", "audio/aac")]
    [InlineData("track.cda", "application/x-cdf")]
    [InlineData("index.php", "application/x-httpd-php")]
    public void CommonWebExtensions_ShouldPreferConventionalMappings(string fileName, string expectedMime)
    {
        MimeHelper.GetMimeType(fileName).ShouldBe(expectedMime);
    }

    [Theory]
    [InlineData("diagram.mermaid", "application/vnd.mermaid")]
    [InlineData("document.typ", "text/vnd.typst")]
    [InlineData("notes.rst", "text/prs.fallenstein.rst")]
    [InlineData("picture.hsj2", "image/hsj2")]
    public void NumberedIanaTemplateExtensions_ShouldBeAvailable(string fileName, string expectedMime)
    {
        MimeHelper.GetMimeType(fileName).ShouldBe(expectedMime);
    }

    [Fact]
    public void GetExtensionsShouldReturnKnownExtensions()
    {
        var jpegExtensions = MimeHelper.GetExtensions(MimeHelper.JPG);
        jpegExtensions.ShouldContain(".jpg");
        jpegExtensions.ShouldContain(".jpeg");
        jpegExtensions.ShouldContain(".jpe");

        const string unknownMime = "application/x-unknown";
        MimeHelper.TryGetExtensions(unknownMime, out _).ShouldBeFalse();
    }

    [Fact]
    public void RuntimeRegistrationShouldUpdateLookups()
    {
        const string extension = "customext";
        const string mime = "application/x-custom";

        try
        {
            MimeHelper.RegisterMimeType(extension, mime);
            MimeHelper.GetMimeType("file.customext").ShouldBe(mime);

            MimeHelper.TryGetExtensions(mime, out var extensions).ShouldBeTrue();
            extensions.ShouldContain(".customext");
        }
        finally
        {
            MimeHelper.UnregisterMimeType(extension).ShouldBeTrue();
            MimeHelper.GetMimeType(extension).ShouldBe(MimeHelper.BIN);
        }
    }
}
