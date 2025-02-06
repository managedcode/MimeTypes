using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private static readonly IDictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);

    static partial void Init();
    static MimeHelper()
    {
        Init();
    }

    public static string GetMimeType(FileInfo file)
    {
        return GetMimeType(file.Extension);
    }

    public static string GetMimeType(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }
        
        var parsedExtension = Path.GetExtension(extension);
        if (!string.IsNullOrEmpty(parsedExtension))
        {
            extension = parsedExtension;
        }
        
        extension = extension.Replace('.', '\0');
        return MimeTypes.TryGetValue(extension, out var mime) ? mime : "application/octet-stream";
    }
    
    private static readonly Regex XmlPattern = new(@"^(?:application|text)/.*?\+?xml$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex JsonPattern = new(@"^application/(?:.*?\+)?json5?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ArchivePattern = new(@"^application/(?:zip|x-(?:7z|rar|tar|bzip2|gzip)-compressed|gzip|vnd\.rar|octet-stream)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExecutablePattern = new(@"^application/(?:x-msdownload|x-executable|x-msi|x-apple-diskimage|octet-stream)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CertificatePattern = new(@"^application/(?:x-x509-ca-cert|pkix-cert|x-pkcs12)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    // New patterns for specific document types
    private static readonly Regex WordPattern = new(@"^application/(?:msword|vnd\.openxmlformats-officedocument\.wordprocessingml\.|vnd\.ms-word\.|vnd\.oasis\.opendocument\.text)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex SpreadsheetPattern = new(@"^application/(?:vnd\.ms-excel|vnd\.openxmlformats-officedocument\.spreadsheetml\.|vnd\.oasis\.opendocument\.spreadsheet)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PresentationPattern = new(@"^application/(?:vnd\.ms-powerpoint|vnd\.openxmlformats-officedocument\.presentationml\.|vnd\.oasis\.opendocument\.presentation)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex CalendarPattern = new(@"^(?:text/calendar|application/ics)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmailPattern = new(@"^(?:message/(?:rfc822|global)|application/(?:mbox|x-msmessage))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    public static MimeTypeCategory GetMimeCategory(string mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
            return MimeTypeCategory.Unknown;
            
        var m = mime.ToLowerInvariant();
        
        // Check formats that can appear in multiple primary types first
        if (XmlPattern.IsMatch(m)) return MimeTypeCategory.Xml;
        if (JsonPattern.IsMatch(m)) return MimeTypeCategory.Json;
        
        // Primary types
        if (m.StartsWith("video/")) return MimeTypeCategory.Video;
        if (m.StartsWith("audio/")) return MimeTypeCategory.Audio;
        if (m.StartsWith("image/")) return MimeTypeCategory.Image;
        if (m.StartsWith("font/")) return MimeTypeCategory.Font;
        if (m.StartsWith("model/")) return MimeTypeCategory.Model;
        
        // Text types (after checking for XML/JSON)
        if (m.StartsWith("text/")) return MimeTypeCategory.Text;
        
        // Document types
        if (m == "application/pdf") return MimeTypeCategory.Pdf;
        if (SpreadsheetPattern.IsMatch(m)) return MimeTypeCategory.Spreadsheet;
        if (PresentationPattern.IsMatch(m)) return MimeTypeCategory.Presentation;
        if (WordPattern.IsMatch(m)) return MimeTypeCategory.Document;
        
        // Special types
        if (ArchivePattern.IsMatch(m)) return MimeTypeCategory.Archive;
        if (ExecutablePattern.IsMatch(m)) return MimeTypeCategory.Executable;
        if (CertificatePattern.IsMatch(m)) return MimeTypeCategory.Certificate;
        if (CalendarPattern.IsMatch(m)) return MimeTypeCategory.Calendar;
        if (EmailPattern.IsMatch(m)) return MimeTypeCategory.Email;
        
        // Generic application type
        return m.StartsWith("application/") ? MimeTypeCategory.Document : MimeTypeCategory.Unknown;
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
}