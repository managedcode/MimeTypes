using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
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
        return TryGetMappedMimeType(value, out var mime) ? mime : DefaultMimeType;
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
        return KnownMimeTypes;
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
        var input = value.AsSpan().Trim();
        var separator = input.IndexOfAny(QueryFragmentSeparators);
        if (separator >= 0)
        {
            input = input[..separator];
        }

        var fileName = Path.GetFileName(input);
        if (fileName.IsEmpty)
        {
            fileName = input;
        }

        fileName = fileName.Trim();
        var snapshot = Volatile.Read(ref Registry);
        var suffix = fileName;
        while (suffix.IndexOf('.') is var dot && dot >= 0)
        {
            suffix = suffix[(dot + 1)..].TrimStart('.');
            if (TryGetCandidateMetadata(suffix.TrimEnd('.'), snapshot, out info))
            {
                return true;
            }
        }

        return TryGetCandidateMetadata(fileName.Trim('.'), snapshot, out info);
    }

    private static bool TryGetCandidateMetadata(ReadOnlySpan<char> candidate, RegistrySnapshot snapshot, out MimeTypeInfo info)
    {
        candidate = candidate.Trim().TrimStart('.');
        if (candidate.IsEmpty)
        {
            info = null!;
            return false;
        }

        // Preserve the registry's invariant normalization for non-ASCII custom extensions.
        if (!Ascii.IsValid(candidate))
        {
            candidate = NormalizeExtensionKey(candidate.ToString());
        }

        if (TryGetEffectiveMimeType(candidate, snapshot, out var mime) && TryGetMimeTypeInfo(mime, out info))
        {
            return true;
        }

#if NET10_0_OR_GREATER
        return MimeTypeInfosByExtension.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(candidate, out info!);
#else
        return MimeTypeInfosByExtension.TryGetValue(candidate.ToString(), out info!);
#endif
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

        if (Volatile.Read(ref Registry).ExtensionsByMime.TryGetValue(mime.Trim(), out var set) && set.Count > 0)
        {
            extensions = set;
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
        if (string.IsNullOrWhiteSpace(extension) || string.IsNullOrWhiteSpace(mime))
        {
            return;
        }

        var normalized = NormalizeExtensionKey(extension);
        if (normalized.Length == 0)
        {
            return;
        }

        var normalizedMime = mime.Trim();
        BuiltInMimeTypes.TryGetValue(normalized, out var builtInMime);
        var dotted = "." + normalized;
        while (true)
        {
            var snapshot = Volatile.Read(ref Registry);
            var previousMime = TryGetExtensionOverride(snapshot, normalized, out var overriddenMime) ? overriddenMime : builtInMime;
            if (string.Equals(previousMime, normalizedMime, StringComparison.Ordinal))
            {
                return;
            }

            var reverse = snapshot.ExtensionsByMime;
            if (!string.Equals(previousMime, normalizedMime, StringComparison.OrdinalIgnoreCase))
            {
                if (previousMime != null)
                {
                    reverse = RemoveExtensionFromMimeMap(reverse, previousMime, dotted);
                }

                if (!reverse.TryGetValue(normalizedMime, out var extensions))
                {
                    extensions = ImmutableSortedSet.Create<string>(StringComparer.OrdinalIgnoreCase);
                }

                reverse = reverse.SetItem(normalizedMime, extensions.Add(dotted));
            }

            var overrides = SetExtensionOverride(snapshot.Overrides, normalized, normalizedMime,
                remove: string.Equals(normalizedMime, builtInMime, StringComparison.Ordinal));
            var updated = new RegistrySnapshot(overrides, reverse);
            if (ReferenceEquals(Interlocked.CompareExchange(ref Registry, updated, snapshot), snapshot))
            {
                return;
            }
        }
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

        BuiltInMimeTypes.TryGetValue(normalized, out var builtInMime);
        var dotted = "." + normalized;
        while (true)
        {
            var snapshot = Volatile.Read(ref Registry);
            var mime = TryGetExtensionOverride(snapshot, normalized, out var overriddenMime) ? overriddenMime : builtInMime;
            if (mime == null)
            {
                return false;
            }

            var overrides = SetExtensionOverride(snapshot.Overrides, normalized, mime: null, remove: builtInMime == null);
            var updated = new RegistrySnapshot(overrides,
                RemoveExtensionFromMimeMap(snapshot.ExtensionsByMime, mime, dotted));
            if (ReferenceEquals(Interlocked.CompareExchange(ref Registry, updated, snapshot), snapshot))
            {
                return true;
            }
        }
    }

    private static bool TryGetMappedMimeType(string? value, out string mime)
    {
        mime = DefaultMimeType;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var input = value!;
        var snapshot = Volatile.Read(ref Registry);
        if (IsBareExtension(input) && TryGetEffectiveMimeType(input.AsSpan(), snapshot, out var directMime))
        {
            mime = directMime;
            return true;
        }

        var start = 0;
        var end = input.Length;
        while (start < end && char.IsWhiteSpace(input[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(input[end - 1]))
        {
            end--;
        }

        var separatorIndex = input.AsSpan(start, end - start).IndexOfAny(QueryFragmentSeparators);
        if (separatorIndex >= 0)
        {
            end = start + separatorIndex;
        }

        var fileNameStart = start;
        for (var index = end - 1; index >= start; index--)
        {
            if (input[index] is '/' or '\\')
            {
                fileNameStart = index + 1;
                break;
            }
        }

        for (var index = fileNameStart; index < end; index++)
        {
            if (input[index] != '.' || index + 1 >= end)
            {
                continue;
            }

            var candidate = input.AsSpan(index + 1, end - index - 1).Trim('.');
            if (!candidate.IsEmpty && TryGetEffectiveMimeType(candidate, snapshot, out var foundMime))
            {
                mime = foundMime;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEffectiveMimeType(ReadOnlySpan<char> extension, RegistrySnapshot snapshot, out string mime)
    {
        if (TryGetExtensionOverride(snapshot, extension, out var overriddenMime))
        {
            mime = overriddenMime!;
            return overriddenMime != null;
        }

#if NET9_0_OR_GREATER
        return BuiltInMimeTypes.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(extension, out mime!);
#else
        return BuiltInMimeTypes.TryGetValue(extension.ToString(), out mime!);
#endif
    }

    private static bool TryGetExtensionOverride(RegistrySnapshot snapshot, ReadOnlySpan<char> extension, out string? mime)
    {
        // Hash buckets keep span lookups allocation-free and retain exact keys for collision checks.
        if (!snapshot.Overrides.IsEmpty &&
            snapshot.Overrides.TryGetValue(string.GetHashCode(extension, StringComparison.OrdinalIgnoreCase), out var entries))
        {
            foreach (var entry in entries)
            {
                if (extension.Equals(entry.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    mime = entry.Mime;
                    return true;
                }
            }
        }

        mime = null;
        return false;
    }

    private static ImmutableDictionary<int, ImmutableArray<ExtensionOverride>> SetExtensionOverride(
        ImmutableDictionary<int, ImmutableArray<ExtensionOverride>> overrides, string extension, string? mime, bool remove)
    {
        var hash = string.GetHashCode(extension.AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (!overrides.TryGetValue(hash, out var entries))
        {
            return remove ? overrides : overrides.Add(hash, [new(extension, mime)]);
        }

        for (var index = 0; index < entries.Length; index++)
        {
            if (!string.Equals(entries[index].Extension, extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (remove)
            {
                return entries.Length == 1 ? overrides.Remove(hash) : overrides.SetItem(hash, entries.RemoveAt(index));
            }

            return overrides.SetItem(hash, entries.SetItem(index, new(extension, mime)));
        }

        return remove ? overrides : overrides.SetItem(hash, entries.Add(new(extension, mime)));
    }

    private static bool IsBareExtension(string value)
    {
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character is '.' or '/' or '\\' or '?' or '#')
            {
                return false;
            }
        }

        return true;
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

    private static ImmutableDictionary<string, ImmutableSortedSet<string>> RemoveExtensionFromMimeMap(
        ImmutableDictionary<string, ImmutableSortedSet<string>> reverse, string mime, string dottedExtension)
    {
        var updatedSet = reverse[mime].Remove(dottedExtension);
        return updatedSet.Count == 0 ? reverse.Remove(mime) : reverse.SetItem(mime, updatedSet);
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
