using System;
using System.Collections.Generic;
using System.IO;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

/// <summary>
/// Exercises stream-based MIME detection against the local sample set.
/// </summary>
public class StreamProbeTests
{
    private static readonly string SamplesDirectory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "StreamProbe");

    private static readonly IReadOnlyDictionary<string, string> ExpectedMimes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sample.csv"] = MimeHelper.BIN,
            ["sample.docx"] = MimeHelper.ZIP,
            ["sample.gif"] = MimeHelper.GIF,
            ["sample.html"] = MimeHelper.BIN,
            ["sample.jpg"] = MimeHelper.JPG,
            ["sample.json"] = MimeHelper.BIN,
            ["sample.pdf"] = MimeHelper.PDF,
            ["sample.png"] = MimeHelper.PNG,
            ["sample.txt"] = MimeHelper.BIN,
            ["sample.webp"] = MimeHelper.WEBP,
            ["sample.xml"] = MimeHelper.BIN,
            ["sample.zip"] = MimeHelper.ZIP
        };

    public static IEnumerable<object[]> SampleFiles()
    {
        if (!Directory.Exists(SamplesDirectory))
        {
            throw new DirectoryNotFoundException($"Sample directory '{SamplesDirectory}' was not found.");
        }

        foreach (var file in Directory.EnumerateFiles(SamplesDirectory))
        {
            var fileName = Path.GetFileName(file);
            yield return new object[] { fileName, file };
        }
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public void DetectsMimeTypeFromStreamSamples(string fileName, string filePath)
    {
        ExpectedMimes.TryGetValue(fileName, out var expected).ShouldBeTrue($"Missing expectation for sample {fileName}");

        using var stream = File.OpenRead(filePath);
        var detected = MimeHelper.GetMimeTypeByContent(stream);

        detected.ShouldBe(expected);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void FilePathProbe_ShouldStayBelowAnAdditionalFileBufferPerCall()
    {
        var path = Path.GetFullPath(Path.Combine(SamplesDirectory, "sample.pdf"));
        MimeHelper.GetMimeTypeByContent(path).ShouldBe(MimeHelper.PDF);
        const int iterations = 256;
        var matches = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < iterations; index++)
        {
            if (MimeHelper.GetMimeTypeByContent(path) == MimeHelper.PDF)
            {
                matches++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        matches.ShouldBe(iterations);
        allocated.ShouldBeLessThan(iterations * 1_536L);
    }
}
