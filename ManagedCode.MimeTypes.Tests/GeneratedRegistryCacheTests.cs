using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public sealed class GeneratedRegistryCacheTests
{
    [Fact]
    public void GeneratedMimeNames_ShouldBeCompileTimeConstants()
    {
        var png = typeof(MimeHelper).GetField(nameof(MimeHelper.PNG));
        var pdf = typeof(MimeHelper).GetField(nameof(MimeHelper.PDF));

        png.ShouldNotBeNull();
        pdf.ShouldNotBeNull();
        png.IsLiteral.ShouldBeTrue();
        pdf.IsLiteral.ShouldBeTrue();
        png.GetRawConstantValue().ShouldBe("image/png");
        pdf.GetRawConstantValue().ShouldBe("application/pdf");
    }

    [Fact]
    public void EveryGeneratedMapping_ShouldResolveFromTheInitializedCache()
    {
        using var document = LoadJson("mimeTypes.json");
        var checkedMappings = 0;

        foreach (var mapping in document.RootElement.EnumerateObject())
        {
            MimeHelper.GetMimeType("file." + mapping.Name).ShouldBe(mapping.Value.GetString());
            checkedMappings++;
        }

        checkedMappings.ShouldBeGreaterThan(1_700);
    }

    [Fact]
    public void EveryGeneratedMapping_ShouldExistInTheCachedReverseIndex()
    {
        using var document = LoadJson("mimeTypes.json");

        foreach (var mapping in document.RootElement.EnumerateObject())
        {
            var mime = mapping.Value.GetString().ShouldNotBeNull();
            MimeHelper.GetExtensions(mime).ShouldContain("." + mapping.Name.TrimStart('.'));
        }
    }

    [Fact]
    public void GeneratedMetadataCatalog_ShouldBeCompleteUniqueSortedAndQueryable()
    {
        using var source = LoadJson("mimeTypes.metadata.json");
        var catalog = MimeHelper.GetKnownMimeTypes();
        var mimes = catalog.Select(static info => info.Mime).ToArray();

        catalog.Count.ShouldBe(source.RootElement.EnumerateObject().Count());
        mimes.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(mimes.Length);
        mimes.ShouldBe(mimes.OrderBy(static mime => mime, StringComparer.OrdinalIgnoreCase), ignoreOrder: false);

        foreach (var info in catalog)
        {
            MimeHelper.TryGetMimeTypeInfo(info.Mime, out var cached).ShouldBeTrue();
            ReferenceEquals(info, cached).ShouldBeTrue();
        }
    }

    [Fact]
    public void WarmUp_ShouldBeIdempotentAndKeepCacheIdentity()
    {
        var extensions = MimeHelper.GetExtensions(MimeHelper.JPG);
        var metadata = MimeHelper.GetKnownMimeTypes();

        for (var index = 0; index < 100; index++)
        {
            MimeHelper.WarmUp();
        }

        ReferenceEquals(extensions, MimeHelper.GetExtensions(MimeHelper.JPG)).ShouldBeTrue();
        ReferenceEquals(metadata, MimeHelper.GetKnownMimeTypes()).ShouldBeTrue();
    }

    private static JsonDocument LoadJson(string fileName)
    {
        return JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, fileName)));
    }
}
