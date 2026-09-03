using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using ManagedCode.MimeTypes;

namespace ManagedCode.MimeTypes.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class MimeLookupBenchmarks
{
    private readonly MemoryStream _pdf = new("%PDF-1.7\n"u8.ToArray());

    [GlobalSetup]
    public void Setup()
    {
        MimeHelper.WarmUp();
    }

    [Benchmark(Baseline = true)]
    public string GeneratedProperty() => MimeHelper.PNG;

    [Benchmark]
    public string BareExtensionLookup() => MimeHelper.GetMimeType("png");

    [Benchmark]
    public string FileNameLookup() => MimeHelper.GetMimeType("assets/photo.png");

    [Benchmark]
    public string CompoundExtensionLookup() => MimeHelper.GetMimeType("types/module.d.ts");

    [Benchmark]
    public IReadOnlyCollection<string> CachedReverseLookup() => MimeHelper.GetExtensions("image/jpeg");

    [Benchmark]
    public bool CachedMetadataLookup() => MimeHelper.TryGetMimeTypeInfo("application/pdf", out _);

    [Benchmark]
    public IReadOnlyCollection<MimeTypeInfo> CachedMetadataCatalog() => MimeHelper.GetKnownMimeTypes();

    [Benchmark]
    public MimeTypeCategory CategoryLookup() => MimeHelper.GetMimeCategory("application/ld+json");

    [Benchmark]
    public string ContentSignatureLookup()
    {
        _pdf.Position = 0;
        return MimeHelper.GetMimeTypeByContent(_pdf);
    }
}
