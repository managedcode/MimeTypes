using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;

namespace ManagedCode.MimeTypes;

/// <summary>
/// Provides MIME type lookup, detection, and categorisation utilities.
/// </summary>
public static partial class MimeHelper
{
    private readonly record struct BuiltInMimeMapping(string Extension, string Mime);

    private readonly record struct ExtensionOverride(string Extension, string? Mime);

    private sealed record RegistrySnapshot(
        ImmutableDictionary<int, ImmutableArray<ExtensionOverride>> Overrides,
        ImmutableDictionary<string, ImmutableSortedSet<string>> ExtensionsByMime);

    private const int ZipProbeLength = 560;
    private static string _defaultMimeType = BIN;

    private static RegistrySnapshot Registry = new(
        ImmutableDictionary<int, ImmutableArray<ExtensionOverride>>.Empty,
        ImmutableDictionary.Create<string, ImmutableSortedSet<string>>(StringComparer.OrdinalIgnoreCase));
    private static FrozenDictionary<string, string> BuiltInMimeTypes = FrozenDictionary<string, string>.Empty;
    private static ImmutableDictionary<string, MimeTypeInfo> MimeTypeInfos = ImmutableDictionary.Create<string, MimeTypeInfo>(StringComparer.OrdinalIgnoreCase);
    private static FrozenDictionary<string, MimeTypeInfo> MimeTypeInfosByExtension = FrozenDictionary<string, MimeTypeInfo>.Empty;
    private static IReadOnlyCollection<MimeTypeInfo> KnownMimeTypes = ImmutableArray<MimeTypeInfo>.Empty;

    /// <summary>
    /// Gets the MIME type returned when no better match is found.
    /// </summary>
    public static string DefaultMimeType => Volatile.Read(ref _defaultMimeType);

    /// <summary>
    /// Populates the core MIME dictionaries; implemented by generated code.
    /// </summary>
    static partial void Init();

    static MimeHelper()
    {
        Init();
        RefreshContentDetectionSignatures();
        InitializeScriptMimeSet();
    }

    /// <summary>
    /// Ensures that the generated MIME registry and all derived lookup caches are initialized.
    /// Subsequent calls are no-ops. Generated MIME constants do not require registry initialization.
    /// </summary>
    public static void WarmUp()
    {
    }

    private static void InitializeBuiltInRegistry(
        ReadOnlySpan<BuiltInMimeMapping> mappings,
        MimeTypeInfo[] metadata)
    {
        var mimeTypes = new Dictionary<string, string>(mappings.Length, StringComparer.OrdinalIgnoreCase);
        var extensionSets = new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            mimeTypes[mapping.Extension] = mapping.Mime;

            if (!extensionSets.TryGetValue(mapping.Mime, out var extensions))
            {
                extensions = ImmutableSortedSet.CreateBuilder<string>(StringComparer.OrdinalIgnoreCase);
                extensionSets.Add(mapping.Mime, extensions);
            }

            extensions.Add("." + mapping.Extension);
        }

        var extensionsByMime = ImmutableDictionary.CreateBuilder<string, ImmutableSortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in extensionSets)
        {
            extensionsByMime.Add(pair.Key, pair.Value.ToImmutable());
        }

        var mimeTypeInfos = ImmutableDictionary.CreateBuilder<string, MimeTypeInfo>(StringComparer.OrdinalIgnoreCase);
        var mimeTypeInfosByExtension = new Dictionary<string, MimeTypeInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in metadata)
        {
            mimeTypeInfos[info.Mime] = info;

            foreach (var extension in info.Extensions)
            {
                var normalizedExtension = NormalizeExtensionKey(extension);
                if (normalizedExtension.Length > 0)
                {
                    mimeTypeInfosByExtension[normalizedExtension] = info;
                }
            }
        }

        Registry = Registry with { ExtensionsByMime = extensionsByMime.ToImmutable() };
        BuiltInMimeTypes = mimeTypes.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        MimeTypeInfos = mimeTypeInfos.ToImmutable();
        MimeTypeInfosByExtension = mimeTypeInfosByExtension.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        Array.Sort(metadata, static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Mime, right.Mime));
        KnownMimeTypes = Array.AsReadOnly(metadata);
    }

    /// <summary>
    /// Overrides the default MIME type returned when no mapping or signature matches.
    /// </summary>
    /// <param name="mime">The MIME type to use as fallback.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="mime"/> is null, empty, or whitespace.</exception>
    public static void SetDefaultMimeType(string mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            throw new ArgumentException("Default MIME type cannot be null or whitespace.", nameof(mime));
        }

        Volatile.Write(ref _defaultMimeType, mime.Trim());
    }
}
