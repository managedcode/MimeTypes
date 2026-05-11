using System;
using System.Collections.Immutable;
using System.Threading;

namespace ManagedCode.MimeTypes;

/// <summary>
/// Provides MIME type lookup, detection, and categorisation utilities.
/// </summary>
public static partial class MimeHelper
{
    private const int ZipProbeLength = 560;
    private static string _defaultMimeType = BIN;

    private static ImmutableDictionary<string, string> MimeTypes = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase);
    private static ImmutableDictionary<string, ImmutableHashSet<string>> ExtensionsByMime = ImmutableDictionary.Create<string, ImmutableHashSet<string>>(StringComparer.OrdinalIgnoreCase);
    private static ImmutableDictionary<string, MimeTypeInfo> MimeTypeInfos = ImmutableDictionary.Create<string, MimeTypeInfo>(StringComparer.OrdinalIgnoreCase);
    private static ImmutableDictionary<string, MimeTypeInfo> MimeTypeInfosByExtension = ImmutableDictionary.Create<string, MimeTypeInfo>(StringComparer.OrdinalIgnoreCase);

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
        Volatile.Write(ref _defaultMimeType, string.Intern(BIN));
        RefreshScriptMimeSet();
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

        Volatile.Write(ref _defaultMimeType, string.Intern(mime.Trim()));
    }
}
