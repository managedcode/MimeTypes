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
    public void RegisterScriptMimeType_ShouldUpdateCaches()
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
