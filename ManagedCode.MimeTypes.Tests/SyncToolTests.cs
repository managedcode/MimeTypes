using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    public void MergeMappings_ShouldApplySourcePrecedence()
    {
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["conflict"] = "application/existing",
            ["local"] = "application/existing-local"
        };

        var mappings = new[]
        {
            new SourceMapping("conflict", "application/apache", MimeSourceKind.Apache),
            new SourceMapping("conflict", "application/iana", MimeSourceKind.Iana),
            new SourceMapping("conflict", "application/mime-db", MimeSourceKind.MimeDb),
            new SourceMapping("iana-only", "application/iana", MimeSourceKind.Iana),
            new SourceMapping("mjs", "application/x-old", MimeSourceKind.Iana),
            new SourceMapping("mjs", "text/javascript", MimeSourceKind.Curated)
        };

        var preferRemote = MimeTypeSyncTool.MergeMappings(mappings, existing, preferRemote: true);
        preferRemote["conflict"].Mime.ShouldBe("application/mime-db");
        preferRemote["iana-only"].Mime.ShouldBe("application/iana");
        preferRemote["local"].Mime.ShouldBe("application/existing-local");
        preferRemote["mjs"].Mime.ShouldBe("text/javascript");

        var preferExisting = MimeTypeSyncTool.MergeMappings(mappings, existing, preferRemote: false);
        preferExisting["conflict"].Mime.ShouldBe("application/existing");
        preferExisting["mjs"].Mime.ShouldBe("text/javascript");
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
}
