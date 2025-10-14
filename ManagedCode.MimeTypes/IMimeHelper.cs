using System.Collections.Generic;
using System.IO;

namespace ManagedCode.MimeTypes;

/// <summary>
/// Describes the contract for components capable of resolving MIME information.
/// </summary>
public interface IMimeHelper
{
    /// <summary>Resolves the MIME type for the supplied file.</summary>
    /// <param name="file">The file whose name or extension should be analysed.</param>
    /// <returns>The resolved MIME type string.</returns>
    string GetMimeType(FileInfo file);

    /// <summary>Resolves the MIME type for the supplied value.</summary>
    /// <param name="value">A file name, URI, or extension string.</param>
    /// <returns>The resolved MIME type string.</returns>
    string GetMimeType(string? value);

    /// <summary>Detects the MIME type by inspecting the contents of the specified file path.</summary>
    /// <param name="filePath">The absolute or relative path to the file.</param>
    /// <returns>The detected MIME type string.</returns>
    string GetMimeTypeByContent(string filePath);

    /// <summary>Detects the MIME type by inspecting the contents of the supplied stream.</summary>
    /// <param name="fileStream">The stream to analyse.</param>
    /// <returns>The detected MIME type string.</returns>
    string GetMimeTypeByContent(Stream fileStream);

    /// <summary>Retrieves all registered extensions for the given MIME.</summary>
    /// <param name="mime">The MIME identifier.</param>
    /// <returns>A read-only collection of dot-prefixed extensions.</returns>
    IReadOnlyCollection<string> GetExtensions(string mime);

    /// <summary>Attempts to retrieve registered extensions for the given MIME.</summary>
    /// <param name="mime">The MIME identifier.</param>
    /// <param name="extensions">When the call succeeds, receives the registered extensions.</param>
    /// <returns><c>true</c> when extensions were found; otherwise <c>false</c>.</returns>
    bool TryGetExtensions(string mime, out IReadOnlyCollection<string> extensions);

    /// <summary>Registers or overwrites a MIME mapping for the supplied extension.</summary>
    /// <param name="extension">The extension to register (with or without a leading dot).</param>
    /// <param name="mime">The MIME value to associate with the extension.</param>
    void RegisterMimeType(string extension, string mime);

    /// <summary>Unregisters the MIME mapping for the supplied extension.</summary>
    /// <param name="extension">The extension to remove.</param>
    /// <returns><c>true</c> when the mapping existed and was removed; otherwise <c>false</c>.</returns>
    bool UnregisterMimeType(string extension);

    /// <summary>Determines the category for the supplied MIME string.</summary>
    /// <param name="mime">The MIME type to classify.</param>
    /// <returns>A <see cref="MimeTypeCategory"/> describing the MIME type.</returns>
    MimeTypeCategory GetMimeCategory(string mime);
}
