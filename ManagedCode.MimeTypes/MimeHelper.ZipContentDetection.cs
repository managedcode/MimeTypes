using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace ManagedCode.MimeTypes;

public static partial class MimeHelper
{
    private const int MaxPackageMetadataBytes = 256 * 1024;
    private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

    private static string? ReadZipPackageType(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var declared = ReadDeclaredZipMime(archive);
        var opc = ReadOpcPackageType(archive);
        var application = ReadApplicationZipType(archive);
        if ((declared != null && opc != null && declared != opc) ||
            (declared != null && application != null && declared != application) ||
            (opc != null && application != null && opc != application))
        {
            return null;
        }

        return declared ?? opc ?? application;
    }

    private static ZipArchiveEntry? GetUniqueZipEntry(ZipArchive archive, string name)
    {
        ZipArchiveEntry? found = null;
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName != name) continue;
            if (found != null) throw new InvalidDataException("Duplicate package metadata or main part.");
            found = entry;
        }

        return found;
    }

    private static string? ReadDeclaredZipMime(ZipArchive archive)
    {
        var entry = GetUniqueZipEntry(archive, "mimetype");
        if (entry == null || entry.Length is <= 0 or > 256)
        {
            return null;
        }

        using var content = entry.Open();
        var bytes = new byte[257];
        var length = ReadUpTo(content, bytes, bytes.Length);
        if (length != entry.Length)
        {
            return null;
        }

        // Some producers save this text entry with a final line ending.
        var mime = Encoding.ASCII.GetString(bytes, 0, length).TrimEnd('\r', '\n');
        if (string.Equals(mime, EPUB, StringComparison.OrdinalIgnoreCase))
        {
            return EPUB;
        }

        return (mime.StartsWith("application/vnd.oasis.opendocument.", StringComparison.OrdinalIgnoreCase) ||
            mime.StartsWith("application/vnd.sun.xml.", StringComparison.OrdinalIgnoreCase)) &&
            TryGetMimeTypeInfo(mime, out var info) && info.Extensions.Count > 0 ? info.Mime : null;
    }

    private static string? ReadApplicationZipType(ZipArchive archive)
    {
        var manifest = GetUniqueZipEntry(archive, "AndroidManifest.xml");
        if (manifest != null && manifest.Length >= 8)
        {
            using var content = manifest.Open();
            var bytes = new byte[8];
            if (ReadUpTo(content, bytes, bytes.Length) == bytes.Length && bytes.AsSpan(0, 4).SequenceEqual("\u0003\u0000\u0008\u0000"u8))
            {
                return APK;
            }
        }

        var javaManifest = GetUniqueZipEntry(archive, "META-INF/MANIFEST.MF");
        if (javaManifest != null && javaManifest.Length is > 0 and <= MaxPackageMetadataBytes)
        {
            using var content = javaManifest.Open();
            var bytes = new byte[64];
            var length = ReadUpTo(content, bytes, bytes.Length);
            if (bytes.AsSpan(0, length).StartsWith("Manifest-Version: "u8))
            {
                return JAR;
            }
        }

        return null;
    }

    private static string? ReadOpcPackageType(ZipArchive archive)
    {
        var mainPart = ReadOpcMainPart(archive, out var relationshipType);
        var manifest = GetUniqueZipEntry(archive, "[Content_Types].xml");
        if (mainPart == null || GetUniqueZipEntry(archive, mainPart) == null || manifest == null || manifest.Length > MaxPackageMetadataBytes)
        {
            return null;
        }

        using var content = manifest.Open();
        using var reader = XmlReader.Create(content, PackageXmlSettings());

        if (reader.MoveToContent() != XmlNodeType.Element ||
            reader.LocalName != "Types" || reader.NamespaceURI != ContentTypesNamespace)
        {
            return null;
        }

        string? detected = null;
        string? defaultMime = null;
        var hasOverride = false;
        var hasDefault = false;
        var extension = Path.GetExtension(mainPart).TrimStart('.');
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1 ||
                reader.NamespaceURI != ContentTypesNamespace)
            {
                continue;
            }

            var mime = GetOpcPackageMime(reader.GetAttribute("ContentType"), relationshipType);
            if (reader.LocalName == "Default" && string.Equals(reader.GetAttribute("Extension"), extension, StringComparison.OrdinalIgnoreCase))
            {
                if (hasDefault) return null;
                hasDefault = true;
                defaultMime = mime;
                continue;
            }

            var part = reader.GetAttribute("PartName");
            if (reader.LocalName != "Override" || string.IsNullOrEmpty(part) || part[0] != '/' || GetPackagePartName(part) != mainPart)
            {
                continue;
            }

            if (hasOverride)
            {
                return null;
            }

            hasOverride = true;
            detected = mime;
        }

        return hasOverride ? detected : defaultMime;
    }

    private static string? ReadOpcMainPart(ZipArchive archive, out string? relationshipType)
    {
        relationshipType = null;
        const string relationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationships = GetUniqueZipEntry(archive, "_rels/.rels");
        if (relationships == null || relationships.Length > MaxPackageMetadataBytes)
        {
            return null;
        }

        using var content = relationships.Open();
        using var reader = XmlReader.Create(content, PackageXmlSettings());
        if (reader.MoveToContent() != XmlNodeType.Element ||
            reader.LocalName != "Relationships" || reader.NamespaceURI != relationshipsNamespace)
        {
            return null;
        }

        string? mainPart = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1 ||
                reader.LocalName != "Relationship" || reader.NamespaceURI != relationshipsNamespace)
            {
                continue;
            }

            var type = reader.GetAttribute("Type");
            if (GetOpcRelationshipFamily(type) == null)
            {
                continue;
            }

            var targetMode = reader.GetAttribute("TargetMode");
            if (mainPart != null || (targetMode != null && !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            relationshipType = type;
            mainPart = GetPackagePartName(reader.GetAttribute("Target"));
            if (mainPart == null)
            {
                return null;
            }
        }

        return mainPart;
    }

    private static XmlReaderSettings PackageXmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxPackageMetadataBytes
    };

    private static string? GetPackagePartName(string? target)
    {
        if (string.IsNullOrEmpty(target) || target.StartsWith("//", StringComparison.Ordinal) || target.Contains('\\') ||
            !Uri.TryCreate(target, UriKind.Relative, out _) || !Uri.TryCreate(new Uri("https://package.invalid/"), target, out var uri) ||
            uri.Scheme != "https" || uri.Host != "package.invalid" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            return null;
        }

        return Uri.UnescapeDataString(uri.AbsolutePath[1..]);
    }

    private static string? GetOpcRelationshipFamily(string? relationshipType) => relationshipType switch
    {
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" => "office",
        "http://purl.oclc.org/ooxml/officeDocument/relationships/officeDocument" => "office",
        "http://schemas.microsoft.com/visio/2010/relationships/document" => "visio",
        "http://schemas.microsoft.com/xps/2005/06/fixedrepresentation" => "xps",
        "http://schemas.openxps.org/oxps/v1.0/fixedrepresentation" => "oxps",
        "http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel" => "3mf",
        _ => null
    };

    // Main-part content types are defined by the Office Open XML package formats.
    // Return the generated MIME constants so validation uses the catalog's spelling.
    private static string? GetOpcPackageMime(string? contentType, string? relationshipType)
    {
        var family = GetOpcRelationshipFamily(relationshipType);
        if (family != "office")
        {
            return (family, contentType?.ToLowerInvariant()) switch
            {
                ("visio", "application/vnd.ms-visio.drawing.main+xml") => VSDX,
                ("visio", "application/vnd.ms-visio.drawing.macroenabled.main+xml") => VSDM,
                ("visio", "application/vnd.ms-visio.stencil.main+xml") => VSSX,
                ("visio", "application/vnd.ms-visio.stencil.macroenabled.main+xml") => VSSM,
                ("visio", "application/vnd.ms-visio.template.main+xml") => VSTX,
                ("visio", "application/vnd.ms-visio.template.macroenabled.main+xml") => VSTM,
                ("xps", "application/vnd.ms-package.xps-fixeddocumentsequence+xml") => XPS,
                ("oxps", "application/oxps-fixeddocumentsequence+xml") => OXPS,
                ("3mf", "application/vnd.ms-package.3dmanufacturing-3dmodel+xml") => "model/3mf",
                _ => null
            };
        }

        return contentType?.ToLowerInvariant() switch
        {
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml" => DOCX,
            "application/vnd.openxmlformats-officedocument.wordprocessingml.template.main+xml" => DOTX,
            "application/vnd.ms-word.document.macroenabled.main+xml" => DOCM,
            "application/vnd.ms-word.template.macroenabledtemplate.main+xml" => DOTM,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" => XLSX,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml" => XLTX,
            "application/vnd.ms-excel.sheet.macroenabled.main+xml" => XLSM,
            "application/vnd.ms-excel.template.macroenabled.main+xml" => XLTM,
            "application/vnd.ms-excel.addin.macroenabled.main+xml" => XLAM,
            "application/vnd.ms-excel.sheet.binary.macroenabled.main" => XLSB,
            "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml" => PPTX,
            "application/vnd.openxmlformats-officedocument.presentationml.template.main+xml" => POTX,
            "application/vnd.openxmlformats-officedocument.presentationml.slideshow.main+xml" => PPSX,
            "application/vnd.ms-powerpoint.presentation.macroenabled.main+xml" => PPTM,
            "application/vnd.ms-powerpoint.template.macroenabled.main+xml" => POTM,
            "application/vnd.ms-powerpoint.slideshow.macroenabled.main+xml" => PPSM,
            "application/vnd.ms-powerpoint.addin.macroenabled.main+xml" => PPAM,
            "application/vnd.openxmlformats-officedocument.presentationml.slide+xml" => SLDX,
            "application/vnd.ms-powerpoint.slide.macroenabled.main+xml" => SLDM,
            "application/vnd.openxmlformats-officedocument.thememanager+xml" => THMX,
            _ => null
        };
    }
}
