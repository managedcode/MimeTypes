using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private static readonly SearchValues<char> QueryFragmentSeparators = SearchValues.Create("?#");

    /// <summary>
    /// Gets an <see cref="IMimeHelper"/> implementation backed by the static APIs.
    /// </summary>
    public static IMimeHelper Instance { get; } = new MimeHelperAdapter();

    /// <summary>
    /// Resolves the MIME type for the supplied file based on its name.
    /// </summary>
    /// <param name="file">The file whose extension should be analysed.</param>
    /// <returns>A MIME type string, or <see cref="DefaultMimeType"/> if no mapping is found.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="file"/> is null.</exception>
    public static string GetMimeType(FileInfo file)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return GetMimeType(file.Name);
    }

    /// <summary>
    /// Resolves the MIME type for the supplied file name or extension string.
    /// </summary>
    /// <param name="value">A file name, URI, or extension.</param>
    /// <returns>A MIME type string, or <see cref="DefaultMimeType"/> if no mapping is found.</returns>
    public static string GetMimeType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DefaultMimeType;
        }

        foreach (var candidate in EnumerateExtensionCandidates(value!))
        {
            var normalized = NormalizeExtensionKey(candidate);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (MimeTypes.TryGetValue(normalized, out var mime))
            {
                return mime;
            }
        }

        return DefaultMimeType;
    }

    /// <summary>
    /// Retrieves all registered extensions that point to the given MIME type.
    /// </summary>
    /// <param name="mime">The MIME type to inspect.</param>
    /// <returns>An immutable collection of dot-prefixed extensions.</returns>
    public static IReadOnlyCollection<string> GetExtensions(string mime)
    {
        return TryGetExtensions(mime, out var extensions) ? extensions : Array.Empty<string>();
    }

    /// <summary>
    /// Gets the registry metadata known for MIME types bundled with this package.
    /// </summary>
    /// <returns>A snapshot of known MIME metadata records.</returns>
    public static IReadOnlyCollection<MimeTypeInfo> GetKnownMimeTypes()
    {
        return MimeTypeInfos.Values.OrderBy(static info => info.Mime, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// Attempts to retrieve registry metadata for the supplied MIME type.
    /// </summary>
    /// <param name="mime">The MIME type to inspect.</param>
    /// <param name="info">The known registry metadata when the lookup succeeds.</param>
    /// <returns><c>true</c> when metadata exists; otherwise <c>false</c>.</returns>
    public static bool TryGetMimeTypeInfo(string mime, out MimeTypeInfo info)
    {
        info = null!;
        if (string.IsNullOrWhiteSpace(mime))
        {
            return false;
        }

        if (MimeTypeInfos.TryGetValue(mime.Trim(), out var found))
        {
            info = found;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to retrieve registry metadata by file name, URI, or extension.
    /// </summary>
    /// <param name="value">A file name, URI, or extension.</param>
    /// <param name="info">The known registry metadata when the lookup succeeds.</param>
    /// <returns><c>true</c> when metadata exists; otherwise <c>false</c>.</returns>
    public static bool TryGetMimeTypeInfoByExtension(string? value, out MimeTypeInfo info)
    {
        info = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var candidate in EnumerateExtensionCandidates(value!))
        {
            var normalized = NormalizeExtensionKey(candidate);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (MimeTypeInfosByExtension.TryGetValue(normalized, out var found))
            {
                info = found;
                return true;
            }

            if (MimeTypes.TryGetValue(normalized, out var mime) && TryGetMimeTypeInfo(mime, out info))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to retrieve all registered extensions that point to the given MIME type.
    /// </summary>
    /// <param name="mime">The MIME type to inspect.</param>
    /// <param name="extensions">The resulting extension set when the lookup succeeds.</param>
    /// <returns><c>true</c> when extensions exist; otherwise <c>false</c>.</returns>
    public static bool TryGetExtensions(string mime, out IReadOnlyCollection<string> extensions)
    {
        extensions = [];
        if (string.IsNullOrWhiteSpace(mime))
        {
            return false;
        }

        if (ExtensionsByMime.TryGetValue(mime.Trim(), out var set) && set.Count > 0)
        {
            extensions = set.Select(static e => "." + e).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static e => e, StringComparer.OrdinalIgnoreCase).ToArray();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Registers or overwrites a MIME mapping for the supplied extension.
    /// </summary>
    /// <param name="extension">A file extension (with or without a leading dot).</param>
    /// <param name="mime">The MIME value to associate with the extension.</param>
    public static void RegisterMimeType(string extension, string mime)
    {
        RegisterMimeTypeInternal(extension, mime, overwrite: true);
    }

    /// <summary>
    /// Removes a MIME mapping for the supplied extension.
    /// </summary>
    /// <param name="extension">A file extension to delete from the mapping table.</param>
    /// <returns><c>true</c> when the mapping existed and was removed; otherwise <c>false</c>.</returns>
    public static bool UnregisterMimeType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        var normalized = NormalizeExtensionKey(extension);
        if (normalized.Length == 0)
        {
            return false;
        }

        while (true)
        {
            var snapshot = MimeTypes;
            if (!snapshot.TryGetValue(normalized, out var mime))
            {
                return false;
            }

            var updated = snapshot.Remove(normalized);
            if (!ReferenceEquals(Interlocked.CompareExchange(ref MimeTypes, updated, snapshot), snapshot))
            {
                continue;
            }

            RemoveExtensionFromMimeMap(mime, normalized);
            RefreshScriptMimeSet();
            return true;
        }
    }

    private static void RegisterMimeTypeInternal(string extension, string mime)
    {
        RegisterMimeTypeInternal(extension, mime, overwrite: true);
    }

    private static void RegisterMimeTypeInternal(string extension, string mime, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(mime))
        {
            return;
        }

        var normalizedExtension = NormalizeExtensionKey(extension);
        if (normalizedExtension.Length == 0)
        {
            return;
        }

        var normalizedMime = string.Intern(mime.Trim());

        string? previousMime = null;
        string? effectiveMime = null;
        var mappingApplied = false;
        var mappingChanged = false;

        while (true)
        {
            var snapshot = MimeTypes;

            if (snapshot.TryGetValue(normalizedExtension, out var existingMime))
            {
                if (!overwrite)
                {
                    return;
                }

                if (string.Equals(existingMime, normalizedMime, StringComparison.Ordinal))
                {
                    effectiveMime = normalizedMime;
                    mappingApplied = true;
                    break;
                }

                var updated = snapshot.SetItem(normalizedExtension, normalizedMime);
                if (!ReferenceEquals(Interlocked.CompareExchange(ref MimeTypes, updated, snapshot), snapshot))
                {
                    continue;
                }

                previousMime = existingMime;
                effectiveMime = normalizedMime;
                mappingApplied = true;
                mappingChanged = true;
                break;
            }
            else
            {
                var updated = snapshot.Add(normalizedExtension, normalizedMime);
                if (!ReferenceEquals(Interlocked.CompareExchange(ref MimeTypes, updated, snapshot), snapshot))
                {
                    continue;
                }

                effectiveMime = normalizedMime;
                mappingApplied = true;
                mappingChanged = true;
                break;
            }
        }

        if (!mappingApplied || effectiveMime == null)
        {
            return;
        }

        AddExtensionToMimeMap(effectiveMime, normalizedExtension);

        if (mappingChanged && previousMime != null && !string.Equals(previousMime, effectiveMime, StringComparison.Ordinal))
        {
            RemoveExtensionFromMimeMap(previousMime, normalizedExtension);
        }

        if (mappingChanged)
        {
            RefreshScriptMimeSet();
        }
    }

    private static void RegisterMimeTypeInfoInternal(MimeTypeInfo info)
    {
        if (string.IsNullOrWhiteSpace(info.Mime))
        {
            return;
        }

        var normalizedMime = string.Intern(info.Mime.Trim());
        if (!string.Equals(normalizedMime, info.Mime, StringComparison.Ordinal))
        {
            info = info with { Mime = normalizedMime };
        }

        MimeTypeInfos = MimeTypeInfos.SetItem(normalizedMime, info);

        foreach (var extension in info.Extensions)
        {
            var normalizedExtension = NormalizeExtensionKey(extension);
            if (normalizedExtension.Length > 0)
            {
                MimeTypeInfosByExtension = MimeTypeInfosByExtension.SetItem(normalizedExtension, info);
            }
        }
    }

    private static IEnumerable<string> EnumerateExtensionCandidates(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            yield break;
        }

        var separatorIndex = trimmed.AsSpan().IndexOfAny(QueryFragmentSeparators);
        if (separatorIndex >= 0)
        {
            trimmed = trimmed[..separatorIndex];
        }

        string fileName;
        try
        {
            fileName = Path.GetFileName(trimmed);
        }
        catch (ArgumentException)
        {
            fileName = trimmed;
        }

        if (string.IsNullOrEmpty(fileName))
        {
            fileName = trimmed;
        }

        fileName = fileName.Trim();
        if (fileName.Length == 0)
        {
            yield break;
        }

        if (!fileName.Contains('.'))
        {
            var bare = fileName.Trim('.');
            if (bare.Length > 0)
            {
                yield return bare;
            }
            yield break;
        }

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sanitized = fileName.Trim('.');
        var index = fileName.IndexOf('.');
        while (index >= 0 && index < fileName.Length - 1)
        {
            var remainder = fileName[(index + 1)..];
            var candidate = remainder.Trim('.');
            if (candidate.Length > 0 && yielded.Add(candidate))
            {
                yield return candidate;
            }

            index = fileName.IndexOf('.', index + 1);
        }

        if (sanitized.Length > 0 && yielded.Add(sanitized))
        {
            yield return sanitized;
        }
    }

    private static string NormalizeExtensionKey(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var span = extension.AsSpan();

        var start = 0;
        var end = span.Length - 1;

        while (start <= end && char.IsWhiteSpace(span[start]))
        {
            start++;
        }

        while (end >= start && char.IsWhiteSpace(span[end]))
        {
            end--;
        }

        while (start <= end && span[start] == '.')
        {
            start++;
        }

        if (start > end)
        {
            return string.Empty;
        }

        var length = end - start + 1;

        return string.Create(length, (extension, start, length), static (destination, state) =>
        {
            var (source, offset, count) = state;
            for (var i = 0; i < count; i++)
            {
                destination[i] = char.ToLowerInvariant(source[offset + i]);
            }
        });
    }

    private static void AddExtensionToMimeMap(string mime, string extension)
    {
        while (true)
        {
            var snapshot = ExtensionsByMime;
            snapshot.TryGetValue(mime, out var existingSet);
            existingSet ??= ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase);

            var updatedSet = existingSet.Add(extension);
            if (ReferenceEquals(updatedSet, existingSet))
            {
                return;
            }

            var updatedMap = snapshot.SetItem(mime, updatedSet);
            if (ReferenceEquals(Interlocked.CompareExchange(ref ExtensionsByMime, updatedMap, snapshot), snapshot))
            {
                return;
            }
        }
    }

    private static void RemoveExtensionFromMimeMap(string mime, string extension)
    {
        while (true)
        {
            var snapshot = ExtensionsByMime;
            if (!snapshot.TryGetValue(mime, out var existingSet))
            {
                return;
            }

            var updatedSet = existingSet.Remove(extension);
            if (ReferenceEquals(updatedSet, existingSet))
            {
                return;
            }

            var updatedMap = updatedSet.Count == 0
                ? snapshot.Remove(mime)
                : snapshot.SetItem(mime, updatedSet);

            if (ReferenceEquals(Interlocked.CompareExchange(ref ExtensionsByMime, updatedMap, snapshot), snapshot))
            {
                return;
            }
        }
    }

    private sealed class MimeHelperAdapter : IMimeHelper
    {
        public string GetMimeType(FileInfo file) => MimeHelper.GetMimeType(file);
        public string GetMimeType(string? value) => MimeHelper.GetMimeType(value);
        public string GetMimeTypeByContent(string filePath) => MimeHelper.GetMimeTypeByContent(filePath);
        public string GetMimeTypeByContent(Stream fileStream) => MimeHelper.GetMimeTypeByContent(fileStream);
        public IReadOnlyCollection<string> GetExtensions(string mime) => MimeHelper.GetExtensions(mime);
        public bool TryGetExtensions(string mime, out IReadOnlyCollection<string> extensions) => MimeHelper.TryGetExtensions(mime, out extensions);
        public void RegisterMimeType(string extension, string mime) => MimeHelper.RegisterMimeType(extension, mime);
        public bool UnregisterMimeType(string extension) => MimeHelper.UnregisterMimeType(extension);
        public MimeTypeCategory GetMimeCategory(string mime) => MimeHelper.GetMimeCategory(mime);
    }
}
