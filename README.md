![img|300x200](https://raw.githubusercontent.com/managedcode/MimeTypes/main/logo.png)

# MimeTypes

[![.NET](https://github.com/managedcode/MimeTypes/actions/workflows/dotnet.yml/badge.svg)](https://github.com/managedcode/MimeTypes/actions/workflows/dotnet.yml)
[![Coverage Status](https://coveralls.io/repos/github/managedcode/MimeTypes/badge.svg?branch=main&service=github)](https://coveralls.io/github/managedcode/MimeTypes?branch=main)
[![nuget](https://github.com/managedcode/MimeTypes/actions/workflows/nuget.yml/badge.svg?branch=main)](https://github.com/managedcode/MimeTypes/actions/workflows/nuget.yml)
[![CodeQL](https://github.com/managedcode/MimeTypes/actions/workflows/codeql-analysis.yml/badge.svg?branch=main)](https://github.com/managedcode/MimeTypes/actions/workflows/codeql-analysis.yml)

| Version | Package | Description |
| ------- | ------- | ----------- |
|[![NuGet Package](https://img.shields.io/nuget/v/ManagedCode.MimeTypes.svg)](https://www.nuget.org/packages/ManagedCode.MimeTypes)|[ManagedCode.MimeTypes](https://www.nuget.org/packages/ManagedCode.MimeTypes)|Core library|

---

## Why MimeTypes?
MIME (Multipurpose Internet Mail Extensions) values describe the media type of a payload. They appear in HTTP headers, file upload
 workflows, messaging protocols and countless integrations. Unfortunately the canonical values are long strings, which makes code
 prone to typos and hard to validate.

`ManagedCode.MimeTypes` ships a generated helper with more than **1,200** extensions sourced from the [IANA media types registry](https://www.iana.org/assignments/media-types/),
the [jshttp/mime-db](https://github.com/jshttp/mime-db) project, [Apache's canonical `mime.types` list](https://github.com/apache/httpd/blob/trunk/docs/conf/mime.types) and curated overrides, smart heuristics for multi-part extensions (such as `.tar.gz`), runtime registration APIs and rich helpers for detecting and
 categorising data by content.

## Feature overview
* Generated extension → MIME map based on the official IANA registry plus supplemental mime-db/Apache coverage and curated compound extensions such as `tar.gz`, `d.ts`, `ps1`, …
* Generated MIME metadata API for IANA registration status, template URLs, references, extensions, intended usage and parseable magic-number prefixes.
* Rich overrides for lightweight markup and diagram DSLs (AsciiDoc, BibTeX, Org-Mode, PlantUML, Mermaid, Typst, TikZ, …) tailored for AI/document pipelines.
* Reverse lookup API that returns the extensions known for a given MIME value.
* Runtime registration/unregistration so applications can plug in custom corporate formats.
* Content sniffing for common file signatures (PDF, PNG, JPEG, GIF, WebP, MP4, ZIP/OOXML, ODF, APK, etc.) with graceful handling of short or empty streams.
* Extended categorisation enum covering document, audio/video, script, binary, multipart and message families with convenience predicates.
* Safe-by-default mutation model powered by immutable dictionaries, configurable fallback MIME via `MimeHelper.SetDefaultMimeType`, and an `IMimeHelper` abstraction (`MimeHelper.Instance`) for DI scenarios.
* CLI utility to refresh `mimeTypes.json` and `mimeTypes.metadata.json` from IANA, supplemental sources or custom sources.

## Quick start
```csharp
using ManagedCode.MimeTypes;

// Extension based lookup (handles multi-part extensions automatically)
var gzip = MimeHelper.GetMimeType("archive.tar.gz");             // application/gzip
var typeScript = MimeHelper.GetMimeType("module.d.ts");          // application/typescript

// Content-based detection
using var stream = File.OpenRead("report.pdf");
var detected = MimeHelper.GetMimeTypeByContent(stream);           // application/pdf

// Categorisation helpers
if (MimeHelper.IsDocument(detected))
{
    // do something useful
}

// Reverse lookup
var jpegExtensions = MimeHelper.GetExtensions("image/jpeg");     // .jpeg, .jpg, .jpe

// Registry metadata
if (MimeHelper.TryGetMimeTypeInfoByExtension("report.pdf", out var pdfInfo))
{
    var template = pdfInfo.TemplateUrl;                           // IANA registration template URL
    var magic = pdfInfo.MagicSignatures.FirstOrDefault()?.Hex;    // 25 50 44 46 2D
}

// Runtime registration (and clean-up)
MimeHelper.RegisterMimeType("acme", "application/x-acme");
var custom = MimeHelper.GetMimeType("invoice.acme");
MimeHelper.UnregisterMimeType("acme");

// Override the fallback MIME and use the DI-friendly adapter
MimeHelper.SetDefaultMimeType(MimeHelper.JSON);
IMimeHelper helper = MimeHelper.Instance;
var fallback = helper.GetMimeType("file.unknownext");             // application/json (custom fallback)
```

## Keeping the database fresh
A small console utility is included to synchronise `mimeTypes.json` and `mimeTypes.metadata.json` with the IANA media types registry,
supplemental upstream datasets and our curated overrides. The repository also ships a scheduled GitHub Actions workflow that runs the sync
tool weekly, validates the generated database with tests, bumps the package patch version when MIME data changes and opens a pull request
whenever new MIME definitions are published.

```bash
# Update the data file in-place
dotnet run --project ManagedCode.MimeTypes.Sync

# Provide custom sources or output
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project ManagedCode.MimeTypes.Sync -- \
    --iana-source https://www.iana.org/assignments/media-types/media-types.xml \
    --add-source https://example.com/additional-mime-map.json \
    --output ./artifacts/mimeTypes.json \
    --metadata-output ./artifacts/mimeTypes.metadata.json

# Start with a clean supplemental slate and prefer remote registry data over preserved local mappings
dotnet run --project ManagedCode.MimeTypes.Sync -- --reset-sources --prefer-remote
```

Running the tool re-generates the JSON file, which in turn updates the generated helper during the next build.

## Installation
```bash
dotnet add package ManagedCode.MimeTypes
```

## Contributing
Issues and PRs are welcome! Run `dotnet test` before sending a contribution, and feel free to use the sync utility to keep the MIME
catalogue current.
