using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

/// <summary>
/// Validates that the mutable portions of <see cref="MimeHelper"/> behave correctly under concurrent access.
/// </summary>
[Collection(MimeHelperMutableStateCollection.Name)]
public class MimeHelperConcurrencyTests
{
    [Fact]
    public void ConcurrentChangesToOneExtension_ShouldKeepBothIndexesConsistent()
    {
        const string extension = "same-concurrent-extension";
        const string dotted = "." + extension;
        string[] candidates = [MimeHelper.PNG, MimeHelper.PDF, MimeHelper.TXT];

        try
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                Parallel.For(0, 1_024, index =>
                {
                    if (index % 4 == 3)
                    {
                        MimeHelper.UnregisterMimeType(extension);
                    }
                    else
                    {
                        MimeHelper.RegisterMimeType(extension, candidates[index % 4]);
                    }
                });

                var current = MimeHelper.GetMimeType(extension);
                foreach (var candidate in candidates)
                {
                    MimeHelper.GetExtensions(candidate).Contains(dotted).ShouldBe(current == candidate);
                }
            }
        }
        finally
        {
            MimeHelper.UnregisterMimeType(extension);
        }
    }

    [Fact]
    public void ConcurrentRegisterAndLookup_ShouldRemainThreadSafe()
    {
        var extensions = Enumerable.Range(0, 128)
            .Select(index => $"ext{index:D4}")
            .ToArray();

        Parallel.ForEach(extensions, extension =>
        {
            MimeHelper.RegisterMimeType(extension, MimeHelper.TXT);
        });

        try
        {
            Parallel.ForEach(extensions, extension =>
            {
                var mime = MimeHelper.GetMimeType(extension);
                mime.ShouldBe(MimeHelper.TXT);
                MimeHelper.IsText(mime).ShouldBeTrue();
            });
        }
        finally
        {
            foreach (var extension in extensions)
            {
                MimeHelper.UnregisterMimeType(extension);
            }
        }
    }
}
