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
