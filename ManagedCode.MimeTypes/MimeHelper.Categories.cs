using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    [GeneratedRegex(@"^(?:application|text)/.*?\+?xml$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex XmlPattern();

    [GeneratedRegex(@"^application/(?:.*?\+)?json5?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonPattern();

    [GeneratedRegex(@"^application/(?:zip|x-(?:7z|rar|tar|bzip2|gzip)-compressed|gzip|vnd\.rar)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArchivePattern();

    [GeneratedRegex(@"^application/(?:x-msdownload|x-executable|x-msi|x-apple-diskimage)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutablePattern();

    [GeneratedRegex(@"^application/(?:x-x509-ca-cert|pkix-cert|x-pkcs12)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CertificatePattern();

    [GeneratedRegex(@"^(?:text/calendar|application/ics)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CalendarPattern();

    [GeneratedRegex(@"^(?:message/(?:rfc822|global)|application/(?:mbox|x-msmessage))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"^application/(?:msword|vnd\.openxmlformats-officedocument\.wordprocessingml\.|vnd\.ms-word\.|vnd\.oasis\.opendocument\.text)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"^application/(?:vnd\.ms-excel|vnd\.openxmlformats-officedocument\.spreadsheetml\.|vnd\.oasis\.opendocument\.spreadsheet)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpreadsheetPattern();

    [GeneratedRegex(@"^application/(?:vnd\.ms-powerpoint|vnd\.openxmlformats-officedocument\.presentationml\.|vnd\.oasis\.opendocument\.presentation)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PresentationPattern();


    [GeneratedRegex(@"^(?:application|text)/(?:javascript|x-javascript|ecmascript|x-ecmascript|typescript|x-typescript|coffeescript|x-php|php|x-httpd-php|x-sh|x-shellscript|shellscript|x-python|x-ruby|x-perl|x-lua|lua|x-tcl|tcl|x-vbs|vbscript|x-vbscript|powershell|x-powershell|x-powershellscript|x-wsh)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptPattern();


    private static readonly FrozenSet<string> ScriptExactSuffixes = new[]
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
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

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
    public static MimeTypeCategory GetMimeCategory(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return MimeTypeCategory.Unknown;
        }

        var type = mime.AsSpan();
        if (type[^1] == '\n')
        {
            // Regex end anchors also accepted one terminal newline.
            type = type[..^1];
        }

        if (type.EndsWith("xml", StringComparison.OrdinalIgnoreCase) &&
            (type.StartsWith("application/", StringComparison.OrdinalIgnoreCase) ||
             type.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) &&
            !type.Contains('\n'))
        {
            return MimeTypeCategory.Xml;
        }

        const string application = "application/";
        if (type.StartsWith(application, StringComparison.OrdinalIgnoreCase))
        {
            var subtype = type[application.Length..];
            if ((subtype.Equals("json", StringComparison.OrdinalIgnoreCase) ||
                 subtype.Equals("json5", StringComparison.OrdinalIgnoreCase) ||
                 subtype.EndsWith("+json", StringComparison.OrdinalIgnoreCase) ||
                 subtype.EndsWith("+json5", StringComparison.OrdinalIgnoreCase)) &&
                !subtype.Contains('\n'))
            {
                return MimeTypeCategory.Json;
            }
        }

        // The ordinal fast paths cover ASCII MIME tokens; keep the regex case folding for other inputs.
        if (!System.Text.Ascii.IsValid(type))
        {
            if (XmlPattern().IsMatch(mime)) return MimeTypeCategory.Xml;
            if (JsonPattern().IsMatch(mime)) return MimeTypeCategory.Json;
        }

        if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Video;
        if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Audio;
        if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Image;
        if (mime.StartsWith("font/", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Font;
        if (mime.StartsWith("model/", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Model;
        if (mime.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Multipart;
        if (mime.StartsWith("message/", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Message;

        var isScript = IsScriptMime(mime);

        if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return isScript ? MimeTypeCategory.Script : MimeTypeCategory.Text;
        }

        if (mime.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)) return MimeTypeCategory.Pdf;
        if (SpreadsheetPattern().IsMatch(mime)) return MimeTypeCategory.Spreadsheet;
        if (PresentationPattern().IsMatch(mime)) return MimeTypeCategory.Presentation;
        if (WordPattern().IsMatch(mime)) return MimeTypeCategory.Document;

        if (ArchivePattern().IsMatch(mime)) return MimeTypeCategory.Archive;
        if (ExecutablePattern().IsMatch(mime)) return MimeTypeCategory.Executable;
        if (CertificatePattern().IsMatch(mime)) return MimeTypeCategory.Certificate;
        if (CalendarPattern().IsMatch(mime)) return MimeTypeCategory.Calendar;
        if (EmailPattern().IsMatch(mime)) return MimeTypeCategory.Email;

        if (isScript) return MimeTypeCategory.Script;

        if (mime.Equals(DefaultMimeType, StringComparison.OrdinalIgnoreCase) || mime.EndsWith("/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return MimeTypeCategory.Binary;
        }

        if (mime.StartsWith("application/", StringComparison.OrdinalIgnoreCase))
        {
            if (mime.Contains("document", StringComparison.OrdinalIgnoreCase) || mime.Contains("msword", StringComparison.OrdinalIgnoreCase) || mime.Contains("officedocument", StringComparison.OrdinalIgnoreCase))
            {
                return MimeTypeCategory.Document;
            }

            if (mime.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase))
            {
                return MimeTypeCategory.Spreadsheet;
            }

            if (mime.Contains("presentation", StringComparison.OrdinalIgnoreCase))
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

    private static void InitializeScriptMimeSet()
    {
        var builder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mime in BuiltInMimeTypes.Values)
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

        _scriptMimeSet = builder.Count == 0
            ? FrozenSet<string>.Empty
            : builder.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsScriptMime(string mime)
    {
        if (string.IsNullOrEmpty(mime))
        {
            return false;
        }

        if (mime.Contains("postscript", StringComparison.OrdinalIgnoreCase) || mime.Contains("ghostscript", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Classification depends only on the MIME text. Custom values use the same predicate.
        if (_scriptMimeSet.Contains(mime))
        {
            return true;
        }

        return IsScriptMimeCore(mime);
    }

    private static bool IsScriptMimeCore(string mime)
    {
        if (ScriptPattern().IsMatch(mime))
        {
            return true;
        }

        var slashIndex = mime.IndexOf('/');
        if (slashIndex < 0 || slashIndex >= mime.Length - 1)
        {
            return false;
        }

        var suffix = mime.AsSpan(slashIndex + 1);

        if (suffix.Contains("postscript", StringComparison.OrdinalIgnoreCase) || suffix.Contains("ghostscript", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

#if NET9_0_OR_GREATER
        if (ScriptExactSuffixes.GetAlternateLookup<ReadOnlySpan<char>>().Contains(suffix))
#else
        if (ScriptExactSuffixes.Contains(suffix.ToString()))
#endif
        {
            return true;
        }

        foreach (var indicator in ScriptSubstringIndicators)
        {
            if (suffix.Contains(indicator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
