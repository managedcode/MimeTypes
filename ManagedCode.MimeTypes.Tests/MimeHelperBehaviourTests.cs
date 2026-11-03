using System;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

/// <summary>
/// Verifies individual behaviours of <see cref="MimeHelper"/> that modify global state.
/// </summary>
[Collection(MimeHelperMutableStateCollection.Name)]
public class MimeHelperBehaviourTests
{
    [Fact]
    public void DefaultMimeType_ShouldMatchGeneratedBin()
    {
        MimeHelper.DefaultMimeType.ShouldBe(MimeHelper.BIN);
    }

    [Fact]
    public void SetDefaultMimeType_ShouldInfluenceFallbackLookup()
    {
        var original = MimeHelper.DefaultMimeType;
        const string customDefault = "application/x-custom-default";

        try
        {
            MimeHelper.SetDefaultMimeType(customDefault);

            MimeHelper.GetMimeType((string?)null).ShouldBe(customDefault);
            MimeHelper.GetMimeType(string.Empty).ShouldBe(customDefault);
            MimeHelper.GetMimeType("   ").ShouldBe(customDefault);
        }
        finally
        {
            MimeHelper.SetDefaultMimeType(original);
        }
    }

    [Fact]
    public void SetDefaultMimeType_ShouldRejectInvalidValues()
    {
        Should.Throw<ArgumentException>(() => MimeHelper.SetDefaultMimeType(null!));
        Should.Throw<ArgumentException>(() => MimeHelper.SetDefaultMimeType("   "));
    }

    [Fact]
    public void BuiltInMappings_ShouldSupportRemovalOverrideAndRestoration()
    {
        try
        {
            MimeHelper.UnregisterMimeType(".PnG").ShouldBeTrue();
            MimeHelper.GetMimeType("asset.PNG").ShouldBe(MimeHelper.DefaultMimeType);
            MimeHelper.GetExtensions(MimeHelper.PNG).ShouldNotContain(".png");

            MimeHelper.RegisterMimeType(".pNG", MimeHelper.PDF);
            MimeHelper.GetMimeType("asset.PNG").ShouldBe(MimeHelper.PDF);
            MimeHelper.GetExtensions(MimeHelper.PDF).ShouldContain(".png");
        }
        finally
        {
            MimeHelper.RegisterMimeType("png", MimeHelper.PNG);
        }

        MimeHelper.GetMimeType("asset.PNG").ShouldBe(MimeHelper.PNG);
        MimeHelper.GetExtensions(MimeHelper.PNG).ShouldContain(".png");
        MimeHelper.GetExtensions(MimeHelper.PDF).ShouldNotContain(".png");
    }

    [Fact]
    public void CustomMimeValues_ShouldNotEnterTheProcessInternPool()
    {
        const string extension = "intern-probe";
        var mime = $"application/x-test-{Guid.NewGuid():N}";
        var original = MimeHelper.DefaultMimeType;

        try
        {
            MimeHelper.RegisterMimeType(extension, mime);
            MimeHelper.GetMimeType(extension).ShouldBe(mime);
            string.IsInterned(mime).ShouldBeNull();

            var fallback = mime + "-fallback";
            MimeHelper.SetDefaultMimeType(fallback);
            MimeHelper.DefaultMimeType.ShouldBe(fallback);
            string.IsInterned(fallback).ShouldBeNull();
        }
        finally
        {
            MimeHelper.UnregisterMimeType(extension);
            MimeHelper.SetDefaultMimeType(original);
        }
    }

    [Fact]
    public void RegisteredScriptMimeType_ShouldBeClassified()
    {
        const string extension = "myscript";
        const string mime = "application/x-shellscript";

        try
        {
            MimeHelper.RegisterMimeType(extension, mime);

            MimeHelper.GetMimeType(extension).ShouldBe(mime);
            MimeHelper.IsScript(mime).ShouldBeTrue();
        }
        finally
        {
            MimeHelper.UnregisterMimeType(extension);
        }
    }
}
