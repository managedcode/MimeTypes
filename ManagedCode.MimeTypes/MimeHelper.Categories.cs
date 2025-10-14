using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
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

    private static readonly Regex ScriptPattern = new(@"^(?:application|text)/(?:javascript|x-javascript|ecmascript|x-ecmascript|typescript|x-typescript|coffeescript|x-php|php|x-httpd-php|x-sh|x-shellscript|shellscript|x-python|x-ruby|x-perl|x-lua|lua|x-tcl|tcl|x-vbs|vbscript|x-vbscript|powershell|x-powershell|x-powershellscript|x-wsh)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly ImmutableHashSet<string> ScriptSuffixBlacklist = ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
    {
        "postscript",
        "vnd.adobe.postscript",
        "vnd.cups-postscript",
        "x-font-ghostscript"
    });

    private static readonly ImmutableHashSet<string> ScriptExactSuffixes = ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, new[]
    {
        "javascript",
        "x-javascript",
        "ecmascript",
        "x-ecmascript",
        "typescript",
        "x-typescript",
        "coffeescript",
        "x-php",
        "php",
        "x-httpd-php",
        "x-sh",
        "x-shellscript",
        "shellscript",
        "x-python",
        "x-ruby",
        "x-perl",
        "x-lua",
        "lua",
        "x-tcl",
        "tcl",
        "x-vbs",
        "vbscript",
        "x-vbscript",
        "powershell",
        "x-powershell",
        "x-powershellscript",
        "x-wsh"
    });

    private static readonly string[] ScriptSubstringIndicators =
    [
        "script",
        "powershell",
        "python",
        "ruby",
        "perl",
        "php",
        "shell",
        "bash",
        "zsh",
        "ksh",
        "csh",
        "lua",
        "tcl",
        "ps1",
        "psm1",
        "psd1",
        "vbscript",
        "wsh"
    ];

    private static FrozenSet<string> _scriptMimeSet = FrozenSet<string>.Empty;

    /// <summary>
    /// Determines the high-level category for the supplied MIME type string.
    /// </summary>
    /// <param name="mime">The MIME type to classify.</param>
    /// <returns>A <see cref="MimeTypeCategory"/> describing the type.</returns>
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

        var isScript = IsScriptMime(m);

        if (m.StartsWith("text/"))
        {
            return isScript ? MimeTypeCategory.Script : MimeTypeCategory.Text;
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

        if (isScript) return MimeTypeCategory.Script;

        if (m == DefaultMimeType || m.EndsWith("/octet-stream", StringComparison.Ordinal))
        {
            return MimeTypeCategory.Binary;
        }

        if (m.StartsWith("application/"))
        {
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

    /// <summary>Checks whether the supplied MIME describes a video payload.</summary>
    public static bool IsVideo(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Video;
    /// <summary>Checks whether the supplied MIME describes an audio payload.</summary>
    public static bool IsAudio(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Audio;
    /// <summary>Checks whether the supplied MIME describes an image payload.</summary>
    public static bool IsImage(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Image;
    /// <summary>Checks whether the supplied MIME describes a document payload.</summary>
    public static bool IsDocument(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Document;
    /// <summary>Checks whether the supplied MIME describes a PDF payload.</summary>
    public static bool IsPdf(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Pdf;
    /// <summary>Checks whether the supplied MIME describes an archive payload.</summary>
    public static bool IsArchive(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Archive;
    /// <summary>Checks whether the supplied MIME describes plain-text content.</summary>
    public static bool IsText(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Text;
    /// <summary>Checks whether the supplied MIME describes JSON content.</summary>
    public static bool IsJson(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Json;
    /// <summary>Checks whether the supplied MIME describes XML content.</summary>
    public static bool IsXml(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Xml;
    /// <summary>Checks whether the supplied MIME describes a font payload.</summary>
    public static bool IsFont(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Font;
    /// <summary>Checks whether the supplied MIME describes a 3D model payload.</summary>
    public static bool IsModel(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Model;
    /// <summary>Checks whether the supplied MIME describes executable content.</summary>
    public static bool IsExecutable(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Executable;
    /// <summary>Checks whether the supplied MIME describes certificate material.</summary>
    public static bool IsCertificate(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Certificate;
    /// <summary>Checks whether the supplied MIME describes a spreadsheet payload.</summary>
    public static bool IsSpreadsheet(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Spreadsheet;
    /// <summary>Checks whether the supplied MIME describes a presentation payload.</summary>
    public static bool IsPresentation(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Presentation;
    /// <summary>Checks whether the supplied MIME describes calendar data.</summary>
    public static bool IsCalendar(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Calendar;
    /// <summary>Checks whether the supplied MIME describes email content.</summary>
    public static bool IsEmail(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Email;
    /// <summary>Checks whether the supplied MIME describes script content.</summary>
    public static bool IsScript(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Script;
    /// <summary>Checks whether the supplied MIME is considered binary.</summary>
    public static bool IsBinary(string mime) => GetMimeCategory(mime) == MimeTypeCategory.Binary;

    private static void RefreshScriptMimeSet()
    {
        var builder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mime in MimeTypes.Values)
        {
            if (string.IsNullOrWhiteSpace(mime))
            {
                continue;
            }

            var normalized = mime.ToLowerInvariant();
            if (IsScriptMimeCore(normalized))
            {
                builder.Add(normalized);
            }
        }

        var frozen = builder.Count == 0
            ? FrozenSet<string>.Empty
            : builder.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        Volatile.Write(ref _scriptMimeSet, frozen);
    }

    private static bool IsScriptMime(string mime)
    {
        if (string.IsNullOrEmpty(mime))
        {
            return false;
        }

        if (mime.Contains("postscript", StringComparison.Ordinal) || mime.Contains("ghostscript", StringComparison.Ordinal))
        {
            return false;
        }

        if (Volatile.Read(ref _scriptMimeSet).Contains(mime))
        {
            return true;
        }

        return IsScriptMimeCore(mime);
    }

    private static bool IsScriptMimeCore(string mime)
    {
        if (ScriptPattern.IsMatch(mime))
        {
            return true;
        }

        var slashIndex = mime.IndexOf('/');
        if (slashIndex < 0 || slashIndex >= mime.Length - 1)
        {
            return false;
        }

        var suffix = mime[(slashIndex + 1)..];

        if (suffix.Contains("postscript", StringComparison.Ordinal) || suffix.Contains("ghostscript", StringComparison.Ordinal))
        {
            return false;
        }

        if (ScriptSuffixBlacklist.Contains(suffix))
        {
            return false;
        }

        if (ScriptExactSuffixes.Contains(suffix))
        {
            return true;
        }

        foreach (var indicator in ScriptSubstringIndicators)
        {
            if (suffix.Contains(indicator, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
