using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

[Collection(MimeHelperMutableStateCollection.Name)]
public sealed class MimeHelperPerformanceTests
{
    private static readonly TimeSpan HotPathBudget = TimeSpan.FromSeconds(2);

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
        var probe = FindStartupProbe();
        var durations = new List<TimeSpan>();

        for (var index = 0; index < 3; index++)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(probe);

            var stopwatch = Stopwatch.StartNew();
            using var process = Process.Start(startInfo);
            process.ShouldNotBeNull();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            stopwatch.Stop();

            process.ExitCode.ShouldBe(0, error);
            output.Trim().ShouldStartWith("image/png|");
            var allocated = long.Parse(output.Trim().Split('|')[3], System.Globalization.CultureInfo.InvariantCulture);
            allocated.ShouldBeLessThanOrEqualTo(256);
            durations.Add(stopwatch.Elapsed);
        }

        durations.Max().ShouldBeLessThan(TimeSpan.FromSeconds(2));
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
