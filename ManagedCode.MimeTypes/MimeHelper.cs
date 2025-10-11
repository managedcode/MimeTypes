using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private const string DefaultMimeType = "application/octet-stream";
    private const int ZipProbeLength = 560;

    private static readonly ReaderWriterLockSlim SyncRoot = new(LockRecursionPolicy.NoRecursion);
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<string>> ExtensionsByMime = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Regex XmlPattern = new(@"^(?:application|text)/.*?\+?xml$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex JsonPattern = new(@"^application/(?:.*?\+)?json5?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ArchivePattern = new(@"^application/(?:zip|x-(?:7z|rar|tar|bzip2|gzip)-compressed|gzip|vnd\.rar)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExecutablePattern = new(@"^application/(?:x-msdownload|x-executable|x-msi|x-apple-diskimage)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CertificatePattern = new(@"^application/(?:x-x509-ca-cert|pkix-cert|x-pkcs12)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CalendarPattern = new(@"^(?:text/calendar|application/ics)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmailPattern = new(@"^(?:message/(?:rfc822|global)|application/(?:mbox|x-msmessage))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WordPattern = new(@"^application/(?:msword|vnd\.openxmlformats-officedocument\.wordprocessingml\.|vnd\.ms-word\.|vnd\.oasis\.opendocument\.text)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SpreadsheetPattern = new(@"^application/(?:vnd\.ms-excel|vnd\.openxmlformats-officedocument\.spreadsheetml\.|vnd\.oasis\.opendocument\.spreadsheet)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PresentationPattern = new(@"^application/(?:vnd\.ms-powerpoint|vnd\.openxmlformats-officedocument\.presentationml\.|vnd\.oasis\.opendocument\.presentation)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ScriptPattern = new(@"^(?:application|text)/(?:javascript|ecmascript|x-php|x-sh|x-shellscript|x-python|x-ruby|x-perl)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> ScriptMimeSet = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/javascript",
        "application/x-javascript",
        "text/javascript",
        "text/ecmascript",
        "application/x-php",
        "application/x-httpd-php",
        "application/x-sh",
        "application/x-shellscript",
        "text/x-shellscript",
        "text/x-python",
        "application/x-python",
        "text/x-ruby",
        "application/x-ruby",
        "text/x-perl",
        "application/x-perl"
    };

    private readonly struct MagicSignature
    {
        public MagicSignature(byte[] signature, string mime, int offset = 0)
        {
            Signature = signature;
            Mime = mime;
            Offset = offset;
        }

        public byte[] Signature { get; }
        public string Mime { get; }
        public int Offset { get; }
    }

    private static readonly MagicSignature[] MagicSignatures =
    {
        new(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf"),
        new(new byte[] { 0xFF, 0xD8, 0xFF }, "image/jpeg"),
        new(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png"),
        new(new byte[] { 0x47, 0x49, 0x46, 0x38 }, "image/gif"),
        new(new byte[] { 0x42, 0x4D }, "image/bmp"),
        new(new byte[] { 0x49, 0x49, 0x2A, 0x00 }, "image/tiff"),
        new(new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, "image/tiff"),
        new(new byte[] { 0x00, 0x00, 0x01, 0x00 }, "image/x-icon"),
        new(new byte[] { 0x38, 0x42, 0x50, 0x53 }, "image/vnd.adobe.photoshop"),
        new(new byte[] { 0x1F, 0x8B }, "application/gzip"),
        new(new byte[] { 0x42, 0x5A, 0x68 }, "application/x-bzip2"),
        new(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 }, "application/x-rar-compressed"),
        new(new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C }, "application/x-7z-compressed"),
        new(new byte[] { 0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00 }, "application/x-xz"),
        new(new byte[] { 0x4F, 0x67, 0x67, 0x53 }, "audio/ogg"),
        new(new byte[] { 0x66, 0x4C, 0x61, 0x43 }, "audio/flac"),
        new(new byte[] { 0x49, 0x44, 0x33 }, "audio/mpeg"),
        new(new byte[] { 0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66, 0x6F, 0x72, 0x6D, 0x61, 0x74, 0x20, 0x33, 0x00 }, "application/x-sqlite3"),
        new(new byte[] { 0x25, 0x21, 0x50, 0x53 }, "application/postscript"),
        new(new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, "application/x-executable"),
        new(new byte[] { 0x25, 0x21, 0x50, 0x53, 0x2D }, "application/postscript"),
        new(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }, "application/vnd.ms-office"),
        new(new byte[] { 0xFF, 0xFE, 0x3C, 0x00 }, "text/xml", 0),
        new(new byte[] { 0x3C, 0x00, 0x3F, 0x00 }, "text/xml", 0)
    };

    private static readonly int MaxSignatureLength = MagicSignatures.Max(static signature => signature.Offset + signature.Signature.Length);
    private static readonly byte[] RiffSignature = { 0x52, 0x49, 0x46, 0x46 };
    private static readonly byte[] WebpFourCC = { 0x57, 0x45, 0x42, 0x50 };
    private static readonly byte[] AviFourCC = { 0x41, 0x56, 0x49, 0x20 };
    private static readonly byte[] WaveFourCC = { 0x57, 0x41, 0x56, 0x45 };
    private static readonly byte[] FtypFourCC = { 0x66, 0x74, 0x79, 0x70 };
    private static readonly byte[] QuickTimeBrand = Encoding.ASCII.GetBytes("qt  ");
    private static readonly byte[][] Mp4Brands =
    {
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
    };
    private static readonly byte[] TorrentPrefix = Encoding.ASCII.GetBytes("d8:announce");
    private static readonly byte[] ZipSignature = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] ZipEmptySignature = { 0x50, 0x4B, 0x05, 0x06 };
    private static readonly byte[] RarSignature = { 0x52, 0x61, 0x72, 0x21 };
    private static readonly byte[] MzSignature = { 0x4D, 0x5A, 0x90, 0x00 };
    private static readonly byte[] RtfSignature = { 0x7B, 0x5C, 0x72, 0x74 };
    private static readonly int MaxContentSniffLength = Math.Max(MaxSignatureLength, ZipProbeLength);

    static partial void Init();

    static MimeHelper()
    {
        Init();
    }

    public static string GetMimeType(FileInfo file)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return GetMimeType(file.Name);
    }

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

            SyncRoot.EnterReadLock();
            try
            {
                if (MimeTypes.TryGetValue(normalized, out var mime))
                {
                    return mime;
                }
            }
            finally
            {
                SyncRoot.ExitReadLock();
            }
        }

        return DefaultMimeType;
    }

    public static IReadOnlyCollection<string> GetExtensions(string mime)
    {
        return TryGetExtensions(mime, out var extensions) ? extensions : Array.Empty<string>();
    }

    public static bool TryGetExtensions(string mime, out IReadOnlyCollection<string> extensions)
    {
        extensions = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(mime))
        {
            return false;
        }

        SyncRoot.EnterReadLock();
        try
        {
            if (ExtensionsByMime.TryGetValue(mime.Trim(), out var set) && set.Count > 0)
            {
                extensions = set.Select(static e => "." + e).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static e => e, StringComparer.OrdinalIgnoreCase).ToArray();
                return true;
            }
        }
        finally
        {
            SyncRoot.ExitReadLock();
        }

        return false;
    }

    public static void RegisterMimeType(string extension, string mime)
    {
        RegisterMimeTypeInternal(extension, mime, overwrite: true);
    }

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

        SyncRoot.EnterWriteLock();
        try
        {
            if (!MimeTypes.TryGetValue(normalized, out var mime))
            {
                return false;
            }

            var removed = MimeTypes.Remove(normalized);
            if (removed && ExtensionsByMime.TryGetValue(mime, out var set))
            {
                set.Remove(normalized);
                if (set.Count == 0)
                {
                    ExtensionsByMime.Remove(mime);
                }
            }

            return removed;
        }
        finally
        {
            SyncRoot.ExitWriteLock();
        }
    }

    public static string GetMimeTypeByContent(string filePath)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return GetMimeTypeByContent(fileStream);
    }

    public static string GetMimeTypeByContent(Stream fileStream)
    {
        if (fileStream == null)
        {
            throw new ArgumentNullException(nameof(fileStream));
        }

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
                return DefaultMimeType;
            }

            var header = new ReadOnlySpan<byte>(buffer, 0, bytesRead);
            var detected = DetectMimeType(header);
            return detected ?? DefaultMimeType;
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

    public static MimeTypeCategory GetMimeCategory(string mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return MimeTypeCategory.Unknown;
        }

        var m = mime.ToLowerInvariant();

        if (XmlPattern.IsMatch(m)) return MimeTypeCategory.Xml;
        if (JsonPattern.IsMatch(m)) return MimeTypeCategory.Json;

        if (m.StartsWith("video/")) return MimeTypeCategory.Video;
        if (m.StartsWith("audio/")) return MimeTypeCategory.Audio;
        if (m.StartsWith("image/")) return MimeTypeCategory.Image;
        if (m.StartsWith("font/")) return MimeTypeCategory.Font;
        if (m.StartsWith("model/")) return MimeTypeCategory.Model;
        if (m.StartsWith("multipart/")) return MimeTypeCategory.Multipart;
        if (m.StartsWith("message/")) return MimeTypeCategory.Message;

        if (m.StartsWith("text/"))
        {
            if (ScriptPattern.IsMatch(m))
            {
                return MimeTypeCategory.Script;
            }

            return MimeTypeCategory.Text;
        }

        if (m == "application/pdf") return MimeTypeCategory.Pdf;
        if (SpreadsheetPattern.IsMatch(m)) return MimeTypeCategory.Spreadsheet;
        if (PresentationPattern.IsMatch(m)) return MimeTypeCategory.Presentation;
        if (WordPattern.IsMatch(m)) return MimeTypeCategory.Document;

        if (ArchivePattern.IsMatch(m)) return MimeTypeCategory.Archive;
        if (ExecutablePattern.IsMatch(m)) return MimeTypeCategory.Executable;
        if (CertificatePattern.IsMatch(m)) return MimeTypeCategory.Certificate;
        if (CalendarPattern.IsMatch(m)) return MimeTypeCategory.Calendar;
        if (EmailPattern.IsMatch(m)) return MimeTypeCategory.Email;

        if (ScriptMimeSet.Contains(m)) return MimeTypeCategory.Script;

        if (m == DefaultMimeType || m.EndsWith("/octet-stream", StringComparison.Ordinal))
        {
            return MimeTypeCategory.Binary;
        }

        if (m.StartsWith("application/"))
        {
            if (m.Contains("script", StringComparison.Ordinal) || m.Contains("powershell", StringComparison.Ordinal))
            {
                return MimeTypeCategory.Script;
            }

            if (m.Contains("document", StringComparison.Ordinal) || m.Contains("msword", StringComparison.Ordinal) || m.Contains("officedocument", StringComparison.Ordinal))
            {
                return MimeTypeCategory.Document;
            }

            if (m.Contains("spreadsheet", StringComparison.Ordinal))
            {
                return MimeTypeCategory.Spreadsheet;
            }

            if (m.Contains("presentation", StringComparison.Ordinal))
            {
                return MimeTypeCategory.Presentation;
            }

            return MimeTypeCategory.Binary;
        }

        return MimeTypeCategory.Unknown;
    }

    public static bool IsVideo(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Video;
    public static bool IsAudio(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Audio;
    public static bool IsImage(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Image;
    public static bool IsDocument(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Document;
    public static bool IsPdf(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Pdf;
    public static bool IsArchive(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Archive;
    public static bool IsText(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Text;
    public static bool IsJson(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Json;
    public static bool IsXml(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Xml;
    public static bool IsFont(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Font;
    public static bool IsModel(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Model;
    public static bool IsExecutable(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Executable;
    public static bool IsCertificate(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Certificate;
    public static bool IsSpreadsheet(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Spreadsheet;
    public static bool IsPresentation(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Presentation;
    public static bool IsCalendar(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Calendar;
    public static bool IsEmail(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Email;
    public static bool IsScript(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Script;
    public static bool IsBinary(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Binary;

    private static IEnumerable<string> EnumerateExtensionCandidates(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            yield break;
        }

        var separatorIndex = trimmed.IndexOfAny(new[] { '?', '#' });
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
        var index = fileName.IndexOf('.');
        while (index >= 0 && index < fileName.Length - 1)
        {
            var candidate = fileName[(index + 1)..].Trim('.');
            if (candidate.Length > 0 && yielded.Add(candidate))
            {
                yield return candidate;
            }

            index = fileName.IndexOf('.', index + 1);
        }

        var sanitized = fileName.Trim('.');
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

        var normalized = extension.Trim().TrimStart('.');
        return normalized.ToLowerInvariant();
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

        SyncRoot.EnterWriteLock();
        try
        {
            if (MimeTypes.TryGetValue(normalizedExtension, out var existingMime))
            {
                if (!overwrite)
                {
                    return;
                }

                if (!string.Equals(existingMime, normalizedMime, StringComparison.Ordinal))
                {
                    if (ExtensionsByMime.TryGetValue(existingMime, out var existingSet))
                    {
                        existingSet.Remove(normalizedExtension);
                        if (existingSet.Count == 0)
                        {
                            ExtensionsByMime.Remove(existingMime);
                        }
                    }
                }
            }

            MimeTypes[normalizedExtension] = normalizedMime;

            if (!ExtensionsByMime.TryGetValue(normalizedMime, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ExtensionsByMime[normalizedMime] = set;
            }

            set.Add(normalizedExtension);
        }
        finally
        {
            SyncRoot.ExitWriteLock();
        }
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
        foreach (var signature in MagicSignatures)
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

        return DetectComplexSignature(header);
    }

    private static string? DetectComplexSignature(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 12 && header[..4].SequenceEqual(RiffSignature))
        {
            if (header.Length >= 12)
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
        }

        if (header.Length >= 12 && header.Slice(4, Math.Min(4, header.Length - 4)).SequenceEqual(FtypFourCC))
        {
            if (header.Length >= 12)
            {
                var brand = header.Slice(8, Math.Min(4, header.Length - 8));
                if (IsMp4Brand(brand))
                {
                    return "video/mp4";
                }

                if (brand.SequenceEqual(QuickTimeBrand))
                {
                    return "video/quicktime";
                }
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
        // Patterns to search for (ASCII, case-insensitive)
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

    // Helper: case-insensitive ASCII search for pattern in span
    private static bool ContainsAsciiIgnoreCase(ReadOnlySpan<byte> span, ReadOnlySpan<byte> pattern)
    {
        if (pattern.Length == 0 || pattern.Length > span.Length)
            return false;

        for (int i = 0; i <= span.Length - pattern.Length; i++)
        {
            int j = 0;
            for (; j < pattern.Length; j++)
            {
                byte a = span[i + j];
                byte b = pattern[j];
                // ASCII case-insensitive compare
                if (a >= (byte)'A' && a <= (byte)'Z') a = (byte)(a + 32);
                if (b >= (byte)'A' && b <= (byte)'Z') b = (byte)(b + 32);
                if (a != b)
                    break;
            }
            if (j == pattern.Length)
                return true;
        }
        return false;
    }
}
