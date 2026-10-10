using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace ManagedCode.MimeTypes.Tests;

[Collection(MimeHelperMutableStateCollection.Name)]
public sealed class MimeHelperPerformanceTests
{
    private static readonly TimeSpan HotPathBudget = TimeSpan.FromSeconds(2);
    private readonly ITestOutputHelper _output;

    public MimeHelperPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void GeneratedConstants_ShouldBeAllocationFree()
    {
        MimeHelper.WarmUp();
        _ = MimeHelper.PNG;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var length = 0;

        for (var index = 0; index < 100_000; index++)
        {
            length += MimeHelper.PNG.Length;
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        length.ShouldBeGreaterThan(0);
        allocated.ShouldBeLessThanOrEqualTo(128);
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData("png")]
    [InlineData(".png")]
    [InlineData("assets/photo.png")]
    [InlineData("https://cdn.example.test/assets/photo.png?v=1")]
    [InlineData("types/module.d.ts")]
    public void HotExtensionLookups_ShouldStayWithinBudget(string input)
    {
        MimeHelper.GetMimeType(input).ShouldNotBe(MimeHelper.BIN);

        Measure(100_000, () => MimeHelper.GetMimeType(input)).ShouldBeLessThan(HotPathBudget);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ReverseLookup_ShouldReturnTheCachedCollection()
    {
        var first = MimeHelper.GetExtensions(MimeHelper.JPG);
        var second = MimeHelper.GetExtensions(MimeHelper.JPG);

        ReferenceEquals(first, second).ShouldBeTrue();
        Measure(100_000, () => MimeHelper.GetExtensions(MimeHelper.JPG)).ShouldBeLessThan(HotPathBudget);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void MetadataCatalog_ShouldReturnTheCachedSortedCollection()
    {
        var first = MimeHelper.GetKnownMimeTypes();
        var second = MimeHelper.GetKnownMimeTypes();

        ReferenceEquals(first, second).ShouldBeTrue();
        first.Count.ShouldBeGreaterThan(1_000);
        Measure(100_000, MimeHelper.GetKnownMimeTypes).ShouldBeLessThan(HotPathBudget);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void MetadataLookup_ShouldStayWithinBudget()
    {
        MimeHelper.TryGetMimeTypeInfo("application/pdf", out _).ShouldBeTrue();

        Measure(100_000, () => MimeHelper.TryGetMimeTypeInfo("application/pdf", out _)).ShouldBeLessThan(HotPathBudget);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CategoryLookup_ShouldStayWithinBudget()
    {
        MimeHelper.GetMimeCategory("application/ld+json").ShouldBe(MimeTypeCategory.Json);

        Measure(100_000, () => MimeHelper.GetMimeCategory("application/ld+json")).ShouldBeLessThan(HotPathBudget);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ParallelLookups_ShouldStayWithinBudget()
    {
        var stopwatch = Stopwatch.StartNew();
        Parallel.For(0, 100_000, static index =>
        {
            var input = index % 2 == 0 ? "report.pdf" : "image.png";
            MimeHelper.GetMimeType(input).ShouldNotBe(MimeHelper.BIN);
        });
        stopwatch.Stop();

        stopwatch.Elapsed.ShouldBeLessThan(HotPathBudget);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ColdProcessStartup_ShouldStayBelowCatastrophicRegressionBudget()
    {
        var durations = new List<TimeSpan>();

        for (var index = 0; index < 3; index++)
        {
            var result = RunStartupProbe(null, "default");
            result.Mime.ShouldBe("image/png");
            result.Allocated.ShouldBeLessThanOrEqualTo(256);
            durations.Add(result.Duration);
        }

        durations.Max().ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [Trait("Category", "Performance")]
    [InlineData("default", "https://cdn.example.test/assets/photo.png?v=1", "image/png")]
    [InlineData("tiered", "https://cdn.example.test/assets/photo.png?v=1", "image/png")]
    [InlineData("optimized", "https://cdn.example.test/assets/photo.png?v=1", "image/png")]
    [InlineData("default", "png", "image/png")]
    [InlineData("tiered", "assets/photo.png", "image/png")]
    [InlineData("tiered", "HTTPS://cdn.example.test/assets/PHOTO.PNG#preview?file=doc.pdf", "image/png")]
    [InlineData("tiered", "types/module.d.ts", "application/typescript")]
    [InlineData("optimized", "archive.tar.gz?download=1", "application/gzip")]
    [InlineData("tiered", "unknown.no-extension", "application/octet-stream")]
    public void ColdProcessLookups_ShouldPreserveAllocationBudgetAcrossJitModes(
        string runtimeMode, string input, string expectedMime)
    {
        var result = RunStartupProbe(input, runtimeMode);

        result.Mime.ShouldBe(expectedMime);
        result.Allocated.ShouldBeLessThanOrEqualTo(256);
    }

    private (TimeSpan Duration, long Allocated, string Mime) RunStartupProbe(string? input, string runtimeMode)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(FindStartupProbe());
        if (input != null)
        {
            startInfo.ArgumentList.Add(input);
        }

        if (runtimeMode == "tiered")
        {
            startInfo.Environment["DOTNET_TieredCompilation"] = "1";
            startInfo.Environment["DOTNET_TieredPGO"] = "1";
            startInfo.Environment["DOTNET_TC_QuickJitForLoops"] = "1";
            startInfo.Environment["DOTNET_TC_OnStackReplacement"] = "1";
            startInfo.Environment["DOTNET_TC_CallCountingDelayMs"] = "0";
        }
        else if (runtimeMode == "optimized")
        {
            startInfo.Environment["DOTNET_TieredCompilation"] = "0";
        }

        var stopwatch = Stopwatch.StartNew();
        using var process = Process.Start(startInfo);
        process.ShouldNotBeNull();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        stopwatch.Stop();

        process.ExitCode.ShouldBe(0, error);
        var parts = output.Trim().Split('|');
        parts.Length.ShouldBe(4, output);
        var allocated = long.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
        _output.WriteLine($"runtime={runtimeMode}; input={input ?? "default"}; result={output.Trim()}; elapsed={stopwatch.Elapsed}");
        return (stopwatch.Elapsed, allocated, parts[0]);
    }

    private static TimeSpan Measure<T>(int iterations, Func<T> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        for (var index = 0; index < iterations; index++)
        {
            _ = operation();
        }
        stopwatch.Stop();
        return stopwatch.Elapsed;
    }

    private static string FindStartupProbe()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = directory.Parent?.Name ?? "Release";

        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "ManagedCode.MimeTypes.sln")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not locate the repository root.");
        var probe = Path.Combine(
            directory.FullName,
            "ManagedCode.MimeTypes.StartupProbe",
            "bin",
            configuration,
            "net10.0",
            "ManagedCode.MimeTypes.StartupProbe.dll");
        File.Exists(probe).ShouldBeTrue($"Startup probe was not built: {probe}");
        return probe;
    }
}
