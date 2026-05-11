using System.Linq;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class MimeMetadataTests
{
    [Fact]
    public void TryGetMimeTypeInfo_ShouldReturnIanaMetadata()
    {
        MimeHelper.TryGetMimeTypeInfo("application/pdf", out var info).ShouldBeTrue();

        info.Mime.ShouldBe("application/pdf");
        info.IsIanaRegistered.ShouldBeTrue();
        info.Extensions.ShouldContain(".pdf");
        info.TemplateUrl.ShouldNotBeNull();
        info.TemplateUrl!.ShouldContain("https://www.iana.org/assignments/media-types/application/pdf");
        info.MagicSignatures.ShouldContain(static signature => signature.IsBytePrefix && signature.Hex == "25 50 44 46 2D");
    }

    [Fact]
    public void TryGetMimeTypeInfoByExtension_ShouldUseSameCandidateRulesAsMimeLookup()
    {
        MimeHelper.TryGetMimeTypeInfoByExtension("https://cdn.example.com/files/report.PDF?version=1", out var info).ShouldBeTrue();

        info.Mime.ShouldBe(MimeHelper.PDF);
        info.Extensions.ShouldContain(".pdf");
    }

    [Fact]
    public void GetKnownMimeTypes_ShouldIncludeIanaAndSupplementalMetadata()
    {
        var known = MimeHelper.GetKnownMimeTypes();

        known.ShouldContain(static info => info.Mime == "application/pdf" && info.IsIanaRegistered);
        known.ShouldContain(static info => info.Mime == "application/x-7z-compressed" && !info.IsIanaRegistered && info.Source == "apache");
    }

    [Fact]
    public void RuntimeRegistration_ShouldNotCreateGeneratedMetadata()
    {
        const string extension = "metatest";
        const string mime = "application/x-metatest";

        try
        {
            MimeHelper.RegisterMimeType(extension, mime);

            MimeHelper.GetMimeType(extension).ShouldBe(mime);
            MimeHelper.TryGetMimeTypeInfoByExtension(extension, out _).ShouldBeFalse();
        }
        finally
        {
            MimeHelper.UnregisterMimeType(extension);
        }
    }
}
