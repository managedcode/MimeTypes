using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class MimeCategoryTests
{
    [Theory]
    [InlineData("video/mp4", MimeTypeCategory.Video)]
    [InlineData("video/x-msvideo", MimeTypeCategory.Video)]
    [InlineData("audio/mpeg", MimeTypeCategory.Audio)]
    [InlineData("audio/midi", MimeTypeCategory.Audio)]
    [InlineData("image/jpeg", MimeTypeCategory.Image)]
    [InlineData("image/png", MimeTypeCategory.Image)]
    [InlineData("text/plain", MimeTypeCategory.Text)]
    [InlineData("text/html", MimeTypeCategory.Text)]
    [InlineData("font/ttf", MimeTypeCategory.Font)]
    [InlineData("font/woff2", MimeTypeCategory.Font)]
    [InlineData("model/gltf-binary", MimeTypeCategory.Model)]
    [InlineData("application/pdf", MimeTypeCategory.Pdf)]
    public void BasicMimeCategories_ShouldMatch(string mime, MimeTypeCategory expectedCategory)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(expectedCategory);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/ld+json")]
    [InlineData("application/json5")]
    [InlineData("application/activity+json")]
    public void JsonMimeTypes_ShouldBeJson(string mime)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(MimeTypeCategory.Json);
        MimeHelper.IsJson(mime).ShouldBeTrue();
    }

    [Theory]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    [InlineData("application/atom+xml")]
    [InlineData("application/mathml+xml")]
    public void XmlMimeTypes_ShouldBeXml(string mime)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(MimeTypeCategory.Xml);
        MimeHelper.IsXml(mime).ShouldBeTrue();
    }

    [Theory]
    [InlineData("application/zip")]
    [InlineData("application/x-7z-compressed")]
    [InlineData("application/x-rar-compressed")]
    [InlineData("application/gzip")]
    public void ArchiveMimeTypes_ShouldBeArchive(string mime)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(MimeTypeCategory.Archive);
        MimeHelper.IsArchive(mime).ShouldBeTrue();
    }

    [Theory]
    [InlineData("application/x-msdownload")]
    [InlineData("application/x-executable")]
    [InlineData("application/x-msi")]
    [InlineData("application/x-apple-diskimage")]
    public void ExecutableMimeTypes_ShouldBeExecutable(string mime)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(MimeTypeCategory.Executable);
        MimeHelper.IsExecutable(mime).ShouldBeTrue();
    }

    [Theory]
    [InlineData("application/x-x509-ca-cert")]
    [InlineData("application/pkix-cert")]
    [InlineData("application/x-pkcs12")]
    public void CertificateMimeTypes_ShouldBeCertificate(string mime)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(MimeTypeCategory.Certificate);
        MimeHelper.IsCertificate(mime).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void EmptyMimeTypes_ShouldBeUnknown(string mime)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(MimeTypeCategory.Unknown);
    }

    [Theory]
    [InlineData("application/msword")]
    [InlineData("application/vnd.ms-excel")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void DocumentMimeTypes_ShouldBeDocument(string mime)
    {
        MimeHelper.GetMimeCategory(mime).ShouldBe(MimeTypeCategory.Document);
        MimeHelper.IsDocument(mime).ShouldBeTrue();
    }

    [Fact]
    public void InvalidMimeType_ShouldBeUnknown()
    {
        MimeHelper.GetMimeCategory("invalid/mime-type").ShouldBe(MimeTypeCategory.Unknown);
    }
}
