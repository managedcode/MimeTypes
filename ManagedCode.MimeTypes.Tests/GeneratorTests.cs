using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class GeneratorTests
{
    [Fact]
    public void ExtensionsTest()
    {
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
    public void PropertyTest()
    {
        MimeHelper.PDF.Should().Be("application/pdf");
        MimeHelper.GZ.Should().Be("application/gzip");
    }
}