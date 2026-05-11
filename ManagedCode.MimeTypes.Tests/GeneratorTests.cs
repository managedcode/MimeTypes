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
        MimeHelper.PDF.ShouldBe(MimeHelper.GetMimeType(".pdf"));
        MimeHelper.DOCX.ShouldBe(MimeHelper.GetMimeType(".docx"));
        MimeHelper.PNG.ShouldBe(MimeHelper.GetMimeType(".png"));
        MimeHelper.MP4.ShouldBe(MimeHelper.GetMimeType(".mp4"));
        MimeHelper._7Z.ShouldBe(MimeHelper.GetMimeType(".7z"));
    }

    [Fact]
    public void GeneratedDictionaryTest()
    {
        // Test if dictionary is properly initialized
        MimeHelper.GetMimeType(".pdf").ShouldBe(MimeHelper.PDF);
        MimeHelper.GetMimeType(".docx").ShouldBe(MimeHelper.DOCX);
        MimeHelper.GetMimeType(".7z").ShouldBe(MimeHelper._7Z);
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
