using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class SyncToolTests
{
    [Fact]
    public void IanaRegistryParser_ShouldReadRecordsAndTemplateMetadata()
    {
        var registry = MimeTypeSyncTool.ParseIanaRegistry(Encoding.UTF8.GetBytes(IanaXml), "https://www.iana.org/assignments/media-types/media-types.xml");

        registry.Updated.ShouldBe("2026-01-02");
        registry.Entries.Count.ShouldBe(4);

        var pdf = registry.Entries.Single(static entry => entry.Mime == "application/pdf");
        pdf.IsIanaRegistered.ShouldBeTrue();
        pdf.TemplateUrl.ShouldBe("https://www.iana.org/assignments/media-types/application/pdf");
        pdf.References.ShouldContain("rfc:rfc8118");

        MimeTypeSyncTool.ApplyIanaTemplate(pdf, PdfTemplate, pdf.TemplateUrl);

        pdf.Extensions.ShouldContain(".pdf");
        pdf.IntendedUsage.ShouldBe("COMMON");
        pdf.PublishedSpecification.ShouldBe("ISO 32000-2");
        pdf.MagicSignatures.Count.ShouldBe(1);
        pdf.MagicSignatures[0].Bytes.ShouldBe(new[] { 0x25, 0x50, 0x44, 0x46, 0x2D });
        pdf.MagicSignatures[0].Hex.ShouldBe("25 50 44 46 2D");
    }

    [Fact]
    public void IanaTemplateParser_ShouldHandleUppercaseExtensionsMissingExtensionsObsoleteAndUnparseableMagic()
    {
        var registry = MimeTypeSyncTool.ParseIanaRegistry(Encoding.UTF8.GetBytes(IanaXml), "https://www.iana.org/assignments/media-types/media-types.xml");

        var csv = registry.Entries.Single(static entry => entry.Mime == "text/csv");
        MimeTypeSyncTool.ApplyIanaTemplate(csv, CsvTemplate, csv.TemplateUrl);
        csv.Extensions.ShouldContain(".csv");
        csv.MagicSignatures.ShouldBeEmpty();

        var noExtension = registry.Entries.Single(static entry => entry.Mime == "application/no-extension");
        MimeTypeSyncTool.ApplyIanaTemplate(noExtension, NoExtensionTemplate, noExtension.TemplateUrl);
        noExtension.Extensions.ShouldBeEmpty();

        var obsolete = registry.Entries.Single(static entry => entry.Mime == "application/old");
        obsolete.IsObsolete.ShouldBeTrue();
        obsolete.PreferredMime.ShouldBe("application/new");

        var signatures = MimeTypeSyncTool.ParseMagicSignatures("Variable length text marker that cannot be represented as a fixed byte prefix.");
        signatures.Count.ShouldBe(1);
        signatures[0].Raw.ShouldContain("Variable length text marker");
        signatures[0].Bytes.ShouldBeEmpty();
        signatures[0].Hex.ShouldBeNull();
    }

    [Fact]
    public void IanaTemplateParser_ShouldHandleNumberedAdditionalInformationFields()
    {
        var metadata = new MimeMetadata
        {
            Mime = "text/vnd.typst",
            IsIanaRegistered = true,
            Source = "iana"
        };

        MimeTypeSyncTool.ApplyIanaTemplate(metadata, NumberedAdditionalInformationTemplate, "https://www.iana.org/assignments/media-types/text/vnd.typst");

        metadata.Extensions.ShouldContain(".typ");
        metadata.DeprecatedAliases.ShouldContain("text/x-typst");
        metadata.MagicSignatures.ShouldBeEmpty();
    }

    [Fact]
    public void MagicSignatureParser_ShouldKeepMultipleFixedPrefixes()
    {
        var ascii = MimeTypeSyncTool.ParseMagicSignatures("""Files start with "%PDF-" or "%!PS".""");
        ascii.Count.ShouldBe(2);
        ascii.Select(static signature => signature.Hex).ShouldBe(["25 50 44 46 2D", "25 21 50 53"], ignoreOrder: true);

        var hex = MimeTypeSyncTool.ParseMagicSignatures("""
            0x89 0x50 0x4E 0x47
            0xFF 0xD8 0xFF
            """);
        hex.Count.ShouldBe(2);
        hex.Select(static signature => signature.Hex).ShouldBe(["89 50 4E 47", "FF D8 FF"], ignoreOrder: true);
    }

    [Fact]
    public void MagicSignatureParser_ShouldDropExamplePrefixesCoveredByShorterPrefix()
    {
        var signatures = MimeTypeSyncTool.ParseMagicSignatures("""All PDF files start with "%PDF-", e.g., "%PDF-1.7" or "%PDF-2.0".""");

        signatures.Count.ShouldBe(1);
        signatures[0].Hex.ShouldBe("25 50 44 46 2D");
    }

    [Fact]
    public void MagicSignatureParser_ShouldIgnoreQuotedExplanatoryWords()
    {
        var fits = MimeTypeSyncTool.ParseMagicSignatures("""
            "SIMPLE = T"
            Jeff Uphoff contributed database entries for the magic number file used by the Unix "file" command.
            """);

        fits.Count.ShouldBe(1);
        fits[0].Hex.ShouldBe("53 49 4D 50 4C 45 20 3D 20 54");

        var woff = MimeTypeSyncTool.ParseMagicSignatures("""The signature field in the WOFF header MUST contain the "magic number" 0x774F4646 ('wOFF')""");

        woff.Count.ShouldBe(1);
        woff[0].Hex.ShouldBe("77 4F 46 46");
    }

    [Fact]
    public void MagicSignatureParser_ShouldKeepOffsetsForOffsetQualifiedSignatures()
    {
        var signatures = MimeTypeSyncTool.ParseMagicSignatures("""0:0xFEFF4D00, 4:"bwtc", 8:"nomo", 12:"dfsm", 16:"fenw", 20:0x4D01""");

        signatures.Select(static signature => (signature.Offset, signature.Hex)).ShouldBe([
            (0, "FE FF 4D 00"),
            (4, "62 77 74 63"),
            (8, "6E 6F 6D 6F"),
            (12, "64 66 73 6D"),
            (16, "66 65 6E 77"),
            (20, "4D 01")
        ], ignoreOrder: true);
    }

    [Fact]
    public void MergeMappings_ShouldApplySourcePrecedence()
    {
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["conflict"] = "application/existing",
            ["local"] = "application/existing-local"
        };

        var mappings = new[]
        {
            new SourceMapping("conflict", "application/mime-db", MimeSourceKind.MimeDb),
            new SourceMapping("conflict", "application/apache", MimeSourceKind.Apache),
            new SourceMapping("conflict", "application/iana", MimeSourceKind.Iana),
            new SourceMapping("conflict", "application/custom", MimeSourceKind.Custom),
            new SourceMapping("apache-conflict", "application/iana", MimeSourceKind.Iana),
            new SourceMapping("apache-conflict", "application/apache", MimeSourceKind.Apache),
            new SourceMapping("mime-db-conflict", "application/iana", MimeSourceKind.Iana),
            new SourceMapping("mime-db-conflict", "application/mime-db", MimeSourceKind.MimeDb),
            new SourceMapping("curated-conflict", "application/iana", MimeSourceKind.Iana),
            new SourceMapping("curated-conflict", "application/curated", MimeSourceKind.Curated),
            new SourceMapping("iana-only", "application/iana", MimeSourceKind.Iana)
        };

        var preferRemote = MimeTypeSyncTool.MergeMappings(mappings, existing, preferRemote: true);
        preferRemote["conflict"].Mime.ShouldBe("application/custom");
        preferRemote["apache-conflict"].Mime.ShouldBe("application/apache");
        preferRemote["mime-db-conflict"].Mime.ShouldBe("application/mime-db");
        preferRemote["curated-conflict"].Mime.ShouldBe("application/curated");
        preferRemote["iana-only"].Mime.ShouldBe("application/iana");
        preferRemote["local"].Mime.ShouldBe("application/existing-local");

        var preferExisting = MimeTypeSyncTool.MergeMappings(mappings, existing, preferRemote: false);
        preferExisting["conflict"].Mime.ShouldBe("application/existing");
        preferExisting["curated-conflict"].Mime.ShouldBe("application/curated");
    }

    [Fact]
    public void CuratedMimeTypesFile_ShouldBeValidDocumentedAndUnique()
    {
        var path = FindCuratedMimeTypesPath();
        var data = File.ReadAllBytes(path);
        var mappings = MimeTypeSyncTool.ParseSupplementalSource(path, data, MimeSourceKind.Curated);

        mappings.ShouldContain(static mapping => mapping.Extension == "event_stream" && mapping.Mime == "text/event-stream" && mapping.Kind == MimeSourceKind.Curated);
        mappings.Select(static mapping => mapping.Extension).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(mappings.Count);

        using var document = JsonDocument.Parse(data);
        document.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            entry.GetProperty("extension").GetString().ShouldNotBeNullOrWhiteSpace();
            entry.GetProperty("mime").GetString().ShouldNotBeNullOrWhiteSpace();
            entry.GetProperty("source").GetString().ShouldNotBeNullOrWhiteSpace();
            entry.GetProperty("reason").GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void DefaultOptions_ShouldLoadBundledCuratedMappings()
    {
        var options = MimeTypeSyncTool.SyncOptions.Parse(Array.Empty<string>());

        options.UseCurated.ShouldBeTrue();
        options.CuratedSources.ShouldHaveSingleItem();
        File.Exists(options.CuratedSources[0]).ShouldBeTrue();
    }

    private static string FindCuratedMimeTypesPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var sourcePath = Path.Combine(directory.FullName, "ManagedCode.MimeTypes.Sync", "curatedMimeTypes.json");
            if (File.Exists(sourcePath))
            {
                return sourcePath;
            }

            var outputPath = Path.Combine(directory.FullName, "curatedMimeTypes.json");
            if (File.Exists(outputPath))
            {
                return outputPath;
            }
        }

        throw new FileNotFoundException("Could not find curatedMimeTypes.json.");
    }

    private const string IanaXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <registry xmlns="http://www.iana.org/assignments" id="media-types">
          <updated>2026-01-02</updated>
          <registry id="application">
            <record date="2024-01-01" updated="2026-01-01">
              <name>pdf</name>
              <xref type="rfc" data="rfc8118" />
              <file type="template">application/pdf</file>
            </record>
            <record>
              <name>no-extension</name>
              <file type="template">application/no-extension</file>
            </record>
            <record>
              <name>old (OBSOLETED in favor of application/new)</name>
              <file type="template">application/old</file>
            </record>
          </registry>
          <registry id="text">
            <record>
              <name>csv</name>
              <file type="template">text/csv</file>
            </record>
          </registry>
        </registry>
        """;

    private const string PdfTemplate = """
        Type name: application

        Subtype name: pdf

        Encoding considerations: binary

        Published specification: ISO 32000-2

        Additional information:

        Magic number(s): All PDF files start with the characters "%PDF-".

        File extension(s): .pdf

        Intended usage: COMMON
        """;

    private const string CsvTemplate = """
        Type name: text

        Subtype name: csv

        Magic number(s): none

        File extension(s): CSV
        """;

    private const string NoExtensionTemplate = """
        Type name: application

        Subtype name: no-extension

        File extension(s): none
        """;

    private const string NumberedAdditionalInformationTemplate = """
        Type name: text

        Subtype name: vnd.typst

        Published specification: https://typst.app/docs/reference/

        Additional information:

        1. Deprecated alias names for this type: text/x-typst
        2. Magic number(s): none
        3. File extension(s): .typ
        4. Macintosh file type code: none
        """;
}
