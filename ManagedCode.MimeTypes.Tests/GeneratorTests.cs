using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class GeneratorTests
{
    [Fact]
    public void ExtensionsTest()
    {
        MimeHelper.GetMimeType("somefile.pdf").ShouldBe("application/pdf");
        MimeHelper.GetMimeType("pdf").ShouldBe("application/pdf");
        MimeHelper.GetMimeType(".gz").ShouldBe("application/gzip");
        MimeHelper.GetMimeType("word.docx").ShouldBe("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        MimeHelper.GetMimeType("C:\\\\users\\file.txt").ShouldBe("text/plain");
    }
    
    [Fact]
    public void EmptyExtensionsTest()
    {
        MimeHelper.GetMimeType("").ShouldBe("application/octet-stream");
        MimeHelper.GetMimeType("     ").ShouldBe("application/octet-stream");
        MimeHelper.GetMimeType(null as string).ShouldBe("application/octet-stream");
    }
    
    [Fact]
    public void GeneratedPropertiesTest()
    {
        // Test static properties generated from mimeTypes.json
        MimeHelper.PDF.ShouldBe("application/pdf");
        MimeHelper.DOCX.ShouldBe("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        MimeHelper.PNG.ShouldBe("image/png");
        MimeHelper.MP4.ShouldBe("video/mp4");
        MimeHelper._7Z.ShouldBe("application/x-7z-compressed");
    }

    [Fact]
    public void GeneratedDictionaryTest()
    {
        // Test if dictionary is properly initialized
        MimeHelper.GetMimeType(".pdf").ShouldBe("application/pdf");
        MimeHelper.GetMimeType(".docx").ShouldBe("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        MimeHelper.GetMimeType(".7z").ShouldBe("application/x-7z-compressed");
    }
}