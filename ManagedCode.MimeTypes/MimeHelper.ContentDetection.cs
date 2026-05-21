using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private readonly record struct MagicSignature(byte[] Signature, string Mime, int Offset = 0);

    private static readonly MagicSignature[] BuiltInMagicSignatures =
    {
        new([0x25, 0x50, 0x44, 0x46, 0x2D], "application/pdf"),
        new([0xFF, 0xD8, 0xFF], "image/jpeg"),
        new([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png"),
        new([0x47, 0x49, 0x46, 0x38], "image/gif"),
        new([0x42, 0x4D], "image/bmp"),
        new([0x49, 0x49, 0x2A, 0x00], "image/tiff"),
        new([0x4D, 0x4D, 0x00, 0x2A], "image/tiff"),
        new([0x00, 0x00, 0x01, 0x00], "image/x-icon"),
        new([0x38, 0x42, 0x50, 0x53], "image/vnd.adobe.photoshop"),
        new([0x1F, 0x8B], "application/gzip"),
        new([0x42, 0x5A, 0x68], "application/x-bzip2"),
        new([0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00], "application/x-rar-compressed"),
        new([0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], "application/x-7z-compressed"),
        new([0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00], "application/x-xz"),
        new([0x4F, 0x67, 0x67, 0x53], "audio/ogg"),
        new([0x66, 0x4C, 0x61, 0x43], "audio/flac"),
        new([0x49, 0x44, 0x33], "audio/mpeg"),
        new([0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x20, 0x33, 0x00], "application/x-sqlite3"),
        new([0x25, 0x21, 0x50, 0x53], "application/postscript"),
        new([0x7F, 0x45, 0x4C, 0x46], "application/x-executable"),
        new([0x25, 0x21, 0x50, 0x53, 0x2D], "application/postscript"),
        new([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], "application/vnd.ms-office"),
        new([0xFF, 0xFE, 0x3C, 0x00], "text/xml", 0),
        new([0x3C, 0x00, 0x3F, 0x00], "text/xml", 0)
    };

    private static readonly int BuiltInMaxSignatureLength = BuiltInMagicSignatures.Max(static signature => signature.Offset + signature.Signature.Length);
    private static MagicSignature[] RegistryMagicSignatures = Array.Empty<MagicSignature>();
    private static int MaxContentSniffLength = Math.Max(BuiltInMaxSignatureLength, ZipProbeLength);
    private static readonly byte[] RiffSignature = [0x52, 0x49, 0x46, 0x46];
    private static readonly byte[] WebpFourCC = [0x57, 0x45, 0x42, 0x50];
    private static readonly byte[] AviFourCC = [0x41, 0x56, 0x49, 0x20];
    private static readonly byte[] WaveFourCC = [0x57, 0x41, 0x56, 0x45];
    private static readonly byte[] FtypFourCC = [0x66, 0x74, 0x79, 0x70];
    private static readonly byte[] QuickTimeBrand = Encoding.ASCII.GetBytes("qt  ");
    private static readonly byte[][] Mp4Brands =
    [
        Encoding.ASCII.GetBytes("isom"),
        Encoding.ASCII.GetBytes("iso2"),
        Encoding.ASCII.GetBytes("avc1"),
        Encoding.ASCII.GetBytes("mp41"),
        Encoding.ASCII.GetBytes("mp42"),
        Encoding.ASCII.GetBytes("dash"),
        Encoding.ASCII.GetBytes("mmp4"),
        Encoding.ASCII.GetBytes("MSNV"),
        Encoding.ASCII.GetBytes("M4V "),
        Encoding.ASCII.GetBytes("MP4V"),
        Encoding.ASCII.GetBytes("3gp4")
    ];
    private static readonly byte[] TorrentPrefix = Encoding.ASCII.GetBytes("d8:announce");
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] ZipEmptySignature = [0x50, 0x4B, 0x05, 0x06];
    private static readonly byte[] RarSignature = [0x52, 0x61, 0x72, 0x21];
    private static readonly byte[] MzSignature = [0x4D, 0x5A, 0x90, 0x00];
    private static readonly byte[] RtfSignature = [0x7B, 0x5C, 0x72, 0x74];

    private static void RefreshContentDetectionSignatures()
    {
        var registrySignatures = BuildRegistryMagicSignatures();
        RegistryMagicSignatures = registrySignatures;

        var maxRegistryLength = registrySignatures.Length == 0
            ? 0
            : registrySignatures.Max(static signature => signature.Offset + signature.Signature.Length);

        MaxContentSniffLength = Math.Max(Math.Max(BuiltInMaxSignatureLength, maxRegistryLength), ZipProbeLength);
    }

    /// <summary>
    /// Detects the MIME type of a file by inspecting its binary signature.
    /// </summary>
    /// <param name="filePath">The path to the file whose content should be analysed.</param>
    /// <returns>The detected MIME type or <see cref="DefaultMimeType"/> when no signature matches.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
    public static string GetMimeTypeByContent(string filePath)
    {
        return TryGetMimeTypeByContent(filePath, out var mime) ? mime : DefaultMimeType;
    }

    /// <summary>
    /// Attempts to detect the MIME type of a file by inspecting its binary signature.
    /// </summary>
    /// <param name="filePath">The path to the file whose content should be analysed.</param>
    /// <param name="mime">The detected MIME type when the call succeeds; otherwise <see cref="DefaultMimeType"/>.</param>
    /// <returns><c>true</c> when a known content signature matches; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
    public static bool TryGetMimeTypeByContent(string filePath, out string mime)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return TryGetMimeTypeByContent(fileStream, out mime);
    }

    /// <summary>
    /// Detects the MIME type of a stream by inspecting its initial bytes.
    /// </summary>
    /// <param name="fileStream">The stream whose content should be analysed.</param>
    /// <returns>The detected MIME type or <see cref="DefaultMimeType"/> when no signature matches.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStream"/> is null.</exception>
    public static string GetMimeTypeByContent(Stream fileStream)
    {
        return TryGetMimeTypeByContent(fileStream, out var mime) ? mime : DefaultMimeType;
    }

    /// <summary>
    /// Attempts to detect the MIME type of a stream by inspecting its initial bytes.
    /// </summary>
    /// <param name="fileStream">The stream whose content should be analysed.</param>
    /// <param name="mime">The detected MIME type when the call succeeds; otherwise <see cref="DefaultMimeType"/>.</param>
    /// <returns><c>true</c> when a known content signature matches; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStream"/> is null.</exception>
    public static bool TryGetMimeTypeByContent(Stream fileStream, out string mime)
    {
        if (fileStream == null)
        {
            throw new ArgumentNullException(nameof(fileStream));
        }

        mime = DefaultMimeType;
        var buffer = ArrayPool<byte>.Shared.Rent(MaxContentSniffLength);
        long? position = null;

        try
        {
            if (fileStream.CanSeek)
            {
                position = fileStream.Position;
            }

            var bytesRead = ReadUpTo(fileStream, buffer, MaxContentSniffLength);

            if (position.HasValue && fileStream.CanSeek)
            {
                fileStream.Seek(position.Value, SeekOrigin.Begin);
            }

            if (bytesRead <= 0)
            {
                return false;
            }

            var header = new ReadOnlySpan<byte>(buffer, 0, bytesRead);
            var detected = DetectMimeType(header);
            if (detected == null)
            {
                return false;
            }

            mime = detected;
            return true;
        }
        finally
        {
            if (position.HasValue && fileStream.CanSeek)
            {
                fileStream.Seek(position.Value, SeekOrigin.Begin);
            }

            ArrayPool<byte>.Shared.Return(buffer, true);
        }
    }

    /// <summary>
    /// Determines whether a file's content signature matches the expected MIME type.
    /// </summary>
    /// <param name="filePath">The path to the file whose content should be analysed.</param>
    /// <param name="expectedMime">The MIME type expected for the file content.</param>
    /// <returns><c>true</c> when content detection succeeds and matches <paramref name="expectedMime"/>; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
    public static bool MatchesMimeTypeByContent(string filePath, string expectedMime)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return MatchesMimeTypeByContent(fileStream, expectedMime);
    }

    /// <summary>
    /// Determines whether a stream's content signature matches the expected MIME type.
    /// </summary>
    /// <param name="fileStream">The stream whose content should be analysed.</param>
    /// <param name="expectedMime">The MIME type expected for the stream content.</param>
    /// <returns><c>true</c> when content detection succeeds and matches <paramref name="expectedMime"/>; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStream"/> is null.</exception>
    public static bool MatchesMimeTypeByContent(Stream fileStream, string expectedMime)
    {
        if (fileStream == null)
        {
            throw new ArgumentNullException(nameof(fileStream));
        }

        return !string.IsNullOrWhiteSpace(expectedMime) &&
            TryGetMimeTypeByContent(fileStream, out var detectedMime) &&
            IsSameMimeType(detectedMime, expectedMime);
    }

    /// <summary>
    /// Determines whether a file's content signature matches the MIME type implied by its extension.
    /// </summary>
    /// <param name="filePath">The file path whose extension and content should be compared.</param>
    /// <returns><c>true</c> when the file has a known extension and its content signature matches that MIME type; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
    public static bool MatchesExtensionByContent(string filePath)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        return TryGetMappedMimeType(filePath, out var expectedMime) &&
            MatchesMimeTypeByContent(filePath, expectedMime);
    }

    /// <summary>
    /// Determines whether a stream's content signature matches the MIME type implied by a file name or extension.
    /// </summary>
    /// <param name="fileName">A file name, URI, or extension used to resolve the expected MIME type.</param>
    /// <param name="fileStream">The stream whose content should be analysed.</param>
    /// <returns><c>true</c> when the name has a known extension and the content signature matches that MIME type; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fileStream"/> is null.</exception>
    public static bool MatchesExtensionByContent(string? fileName, Stream fileStream)
    {
        if (fileStream == null)
        {
            throw new ArgumentNullException(nameof(fileStream));
        }

        return TryGetMappedMimeType(fileName, out var expectedMime) &&
            MatchesMimeTypeByContent(fileStream, expectedMime);
    }

    private static MagicSignature[] BuildRegistryMagicSignatures()
    {
        var candidates = new List<MagicSignature>();
        foreach (var info in MimeTypeInfos.Values)
        {
            foreach (var signature in info.MagicSignatures)
            {
                if (signature.Offset < 0 || !signature.IsBytePrefix)
                {
                    continue;
                }

                candidates.Add(new MagicSignature(signature.Bytes.ToArray(), info.Mime, signature.Offset));
            }
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<MagicSignature>();
        }

        var mimeBySignature = new Dictionary<string, string>(StringComparer.Ordinal);
        var conflictingSignatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (var signature in candidates)
        {
            var key = CreateSignatureKey(signature);
            if (mimeBySignature.TryGetValue(key, out var existingMime))
            {
                if (!string.Equals(existingMime, signature.Mime, StringComparison.OrdinalIgnoreCase))
                {
                    conflictingSignatures.Add(key);
                }

                continue;
            }

            mimeBySignature.Add(key, signature.Mime);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return candidates
            .Where(signature => !conflictingSignatures.Contains(CreateSignatureKey(signature)))
            .Where(signature => seen.Add(CreateSignatureKey(signature) + "|" + signature.Mime))
            .OrderByDescending(static signature => signature.Signature.Length)
            .ThenByDescending(static signature => signature.Offset)
            .ThenBy(static signature => signature.Mime, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CreateSignatureKey(MagicSignature signature)
    {
        return string.Concat(signature.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", Convert.ToHexString(signature.Signature));
    }

    private static int ReadUpTo(Stream stream, byte[] buffer, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, totalRead, count - totalRead);
            if (read <= 0)
            {
                break;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private static string? DetectMimeType(ReadOnlySpan<byte> header)
    {
        return DetectMagicSignature(header, BuiltInMagicSignatures) ??
            DetectComplexSignature(header) ??
            DetectMagicSignature(header, RegistryMagicSignatures);
    }

    private static string? DetectMagicSignature(ReadOnlySpan<byte> header, IReadOnlyList<MagicSignature> signatures)
    {
        foreach (var signature in signatures)
        {
            if (header.Length < signature.Offset + signature.Signature.Length)
            {
                continue;
            }

            if (header.Slice(signature.Offset, signature.Signature.Length).SequenceEqual(signature.Signature))
            {
                return signature.Mime;
            }
        }

        return null;
    }

    private static bool IsSameMimeType(string detectedMime, string expectedMime)
    {
        return string.Equals(detectedMime.Trim(), expectedMime.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? DetectComplexSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 12 && header[..4].SequenceEqual(RiffSignature))
        {
            var format = header.Slice(8, 4);
            if (format.SequenceEqual(WebpFourCC))
            {
                return "image/webp";
            }

            if (format.SequenceEqual(AviFourCC))
            {
                return "video/x-msvideo";
            }

            if (format.SequenceEqual(WaveFourCC))
            {
                return "audio/wav";
            }
        }

        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual(FtypFourCC))
        {
            var brand = header.Slice(8, 4);
            if (IsMp4Brand(brand))
            {
                return "video/mp4";
            }

            if (brand.SequenceEqual(QuickTimeBrand))
            {
                return "video/quicktime";
            }
        }

        if (header.Length >= 4 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
        {
            return "audio/mpeg";
        }

        if (header.Length >= 4 && (header[..4].SequenceEqual(ZipSignature) || header[..4].SequenceEqual(ZipEmptySignature)))
        {
            return DetectZipBasedType(header) ?? "application/zip";
        }

        if (header.Length >= 4 && header[..4].SequenceEqual(RarSignature))
        {
            return "application/x-rar-compressed";
        }

        if (header.Length >= 4 && header[..4].SequenceEqual(MzSignature))
        {
            return "application/x-msdownload";
        }

        if (header.Length >= 4 && header[..4].SequenceEqual(RtfSignature))
        {
            return "application/rtf";
        }

        if (header.Length >= TorrentPrefix.Length && header[..TorrentPrefix.Length].SequenceEqual(TorrentPrefix))
        {
            return "application/x-bittorrent";
        }

        return null;
    }

    private static bool IsMp4Brand(ReadOnlySpan<byte> brand)
    {
        foreach (var known in Mp4Brands)
        {
            if (brand.SequenceEqual(known))
            {
                return true;
            }
        }

        return false;
    }

    private static string? DetectZipBasedType(ReadOnlySpan<byte> header)
    {
        ReadOnlySpan<byte> epubPattern = "mimetypeapplication/epub+zip"u8;
        if (ContainsAsciiIgnoreCase(header, epubPattern))
        {
            return "application/epub+zip";
        }

        ReadOnlySpan<byte> odtPattern = "mimetypeapplication/vnd.oasis.opendocument.text"u8;
        if (ContainsAsciiIgnoreCase(header, odtPattern))
        {
            return "application/vnd.oasis.opendocument.text";
        }

        ReadOnlySpan<byte> odsPattern = "mimetypeapplication/vnd.oasis.opendocument.spreadsheet"u8;
        if (ContainsAsciiIgnoreCase(header, odsPattern))
        {
            return "application/vnd.oasis.opendocument.spreadsheet";
        }

        ReadOnlySpan<byte> odpPattern = "mimetypeapplication/vnd.oasis.opendocument.presentation"u8;
        if (ContainsAsciiIgnoreCase(header, odpPattern))
        {
            return "application/vnd.oasis.opendocument.presentation";
        }

        ReadOnlySpan<byte> wordPattern = "word/"u8;
        if (ContainsAsciiIgnoreCase(header, wordPattern))
        {
            return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        }

        ReadOnlySpan<byte> xlPattern = "xl/"u8;
        if (ContainsAsciiIgnoreCase(header, xlPattern))
        {
            return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }

        ReadOnlySpan<byte> pptPattern = "ppt/"u8;
        if (ContainsAsciiIgnoreCase(header, pptPattern))
        {
            return "application/vnd.openxmlformats-officedocument.presentationml.presentation";
        }

        ReadOnlySpan<byte> androidManifestPattern = "AndroidManifest.xml"u8;
        if (ContainsAsciiIgnoreCase(header, androidManifestPattern))
        {
            return "application/vnd.android.package-archive";
        }

        return null;
    }

    private static bool ContainsAsciiIgnoreCase(ReadOnlySpan<byte> span, ReadOnlySpan<byte> pattern)
    {
        if (pattern.Length == 0 || pattern.Length > span.Length)
        {
            return false;
        }

        for (var i = 0; i <= span.Length - pattern.Length; i++)
        {
            var j = 0;

            for (; j < pattern.Length; j++)
            {
                byte a = span[i + j];
                byte b = pattern[j];

                if ((uint)(a - 'A') <= 'Z' - 'A')
                {
                    a = (byte)(a + 32);
                }

                if ((uint)(b - 'A') <= 'Z' - 'A')
                {
                    b = (byte)(b + 32);
                }

                if (a != b)
                {
                    break;
                }
            }

            if (j == pattern.Length)
            {
                return true;
            }
        }

        return false;
    }
}
