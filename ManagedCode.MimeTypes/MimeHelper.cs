using System;
using System.Collections.Generic;
using System.IO;

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
}