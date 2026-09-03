using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

namespace ManagedCode.MimeTypes.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RegistryInitializationBenchmarks
{
    private (string Extension, string Mime)[] _mappings = null!;
    private byte[] _registryJson = null!;

    [Params(1_700)]
    public int MappingCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _mappings = Enumerable.Range(0, MappingCount)
            .Select(static index => ($"ext{index}", $"application/x-type-{index % 600}"))
            .ToArray();
        _registryJson = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "mimeTypes.json"));
    }

    [Benchmark(Baseline = true)]
    public ImmutableDictionary<string, string> LegacyIncrementalImmutableInitialization()
    {
        var mappings = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in _mappings)
        {
            mappings = mappings.SetItem(mapping.Extension, mapping.Mime);
        }

        return mappings;
    }

    [Benchmark]
    public ImmutableDictionary<string, string> BatchBuilderInitialization()
    {
        var mappings = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in _mappings)
        {
            mappings[mapping.Extension] = mapping.Mime;
        }

        return mappings.ToImmutable();
    }

    [Benchmark]
    public (ImmutableDictionary<string, string> MutableSnapshot, FrozenDictionary<string, string> BuiltInIndex) BatchStaticCachesInitialization()
    {
        var mappings = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mapping in _mappings)
        {
            mappings[mapping.Extension] = mapping.Mime;
        }

        return (mappings.ToImmutable(), mappings.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    [Benchmark]
    public int ParseSourceRegistryJson()
    {
        using var document = JsonDocument.Parse(_registryJson);
        return document.RootElement.GetRawText().Length;
    }
}
