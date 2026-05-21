<img src="https://raw.githubusercontent.com/managedcode/MimeTypes/main/logo.png" alt="MimeTypes logo" width="220">

# MimeTypes

[![CI](https://github.com/managedcode/MimeTypes/actions/workflows/ci.yml/badge.svg)](https://github.com/managedcode/MimeTypes/actions/workflows/ci.yml)
[![Release](https://github.com/managedcode/MimeTypes/actions/workflows/release.yml/badge.svg)](https://github.com/managedcode/MimeTypes/actions/workflows/release.yml)
[![CodeQL](https://github.com/managedcode/MimeTypes/actions/workflows/codeql-analysis.yml/badge.svg)](https://github.com/managedcode/MimeTypes/actions/workflows/codeql-analysis.yml)
[![Codecov](https://codecov.io/gh/managedcode/MimeTypes/branch/main/graph/badge.svg)](https://codecov.io/gh/managedcode/MimeTypes)
[![NuGet](https://img.shields.io/nuget/v/ManagedCode.MimeTypes.svg)](https://www.nuget.org/packages/ManagedCode.MimeTypes)
[![NuGet downloads](https://img.shields.io/nuget/dt/ManagedCode.MimeTypes.svg)](https://www.nuget.org/packages/ManagedCode.MimeTypes)
[![License](https://img.shields.io/nuget/l/ManagedCode.MimeTypes.svg)](https://github.com/managedcode/MimeTypes/blob/main/LICENSE)
[![.NET](https://img.shields.io/badge/.NET-net8.0%20%7C%20net9.0%20%7C%20net10.0-512bd4.svg)](https://github.com/managedcode/MimeTypes/blob/main/ManagedCode.MimeTypes/ManagedCode.MimeTypes.csproj)

`ManagedCode.MimeTypes` is a generated MIME/media type helper for .NET. It combines the IANA media types registry, Apache's maintained `mime.types` data, mime-db gap-fill entries, curated compatibility overrides, and registry metadata such as template URLs, references, extensions, and parseable magic-number prefixes.

Use it when you need to map file names to MIME types, inspect file content signatures, validate upload claims, classify MIME families, or keep an application aligned with the current public MIME registries.

## Contents

- [Installation](#installation)
- [What It Does](#what-it-does)
- [Quick Start](#quick-start)
- [Content Detection](#content-detection)
- [Validating Uploads](#validating-uploads)
- [Registry Metadata](#registry-metadata)
- [Categories](#categories)
- [Reverse Lookup](#reverse-lookup)
- [Runtime Registration](#runtime-registration)
- [Default MIME Type](#default-mime-type)
- [DI-Friendly Adapter](#di-friendly-adapter)
- [Data Sources](#data-sources)
- [Refreshing The Catalog](#refreshing-the-catalog)
- [Development](#development)

## Installation

```bash
dotnet add package ManagedCode.MimeTypes
```

Targets: `net8.0`, `net9.0`, and `net10.0`.

## What It Does

| Area | API examples | Notes |
| --- | --- | --- |
| Extension lookup | `GetMimeType("report.pdf")` | Handles file names, extensions, URLs, query strings, and multi-part extension candidates. |
| Reverse lookup | `GetExtensions("image/jpeg")` | Returns known dot-prefixed extensions for a MIME value. |
| Registry metadata | `TryGetMimeTypeInfo(...)`, `GetKnownMimeTypes()` | Includes IANA registration status, template URLs, references, intended usage, aliases, and magic-number metadata. |
| Content detection | `GetMimeTypeByContent(stream)` | Sniffs common signatures plus parseable registry magic prefixes. Seekable streams are restored to their original position. |
| Content validation | `MatchesMimeTypeByContent(...)`, `MatchesExtensionByContent(...)` | Checks whether detected bytes match an expected MIME type or the MIME implied by a file name. |
| Categorisation | `GetMimeCategory(...)`, `IsImage(...)`, `IsJson(...)` | Groups MIME values into high-level families such as image, archive, script, document, spreadsheet, and executable. |
| Runtime mappings | `RegisterMimeType(...)`, `UnregisterMimeType(...)` | Allows application-specific extensions without regenerating the database. |
| Configuration and DI | `SetDefaultMimeType(...)`, `MimeHelper.Instance` | Configurable fallback MIME and an `IMimeHelper` adapter for common operations. |

## Quick Start

```csharp
using ManagedCode.MimeTypes;

var pdf = MimeHelper.GetMimeType("report.pdf");
var gzip = MimeHelper.GetMimeType("archive.tar.gz");
var avatar = MimeHelper.GetMimeType("https://cdn.example.com/users/42/avatar.png?v=2");

Console.WriteLine(pdf);    // application/pdf
Console.WriteLine(gzip);   // application/gzip
Console.WriteLine(avatar); // image/png
```

Unknown extensions return `MimeHelper.DefaultMimeType`, which defaults to `application/octet-stream`.

```csharp
var fallback = MimeHelper.GetMimeType("file.unknown");
Console.WriteLine(fallback); // application/octet-stream
```

## Content Detection

Extension lookup tells you what a file claims to be. Content detection inspects the first bytes and tells you what the payload looks like.

```csharp
using ManagedCode.MimeTypes;

using var stream = File.OpenRead("report.pdf");

var detected = MimeHelper.GetMimeTypeByContent(stream);
Console.WriteLine(detected); // application/pdf

if (MimeHelper.TryGetMimeTypeByContent(stream, out var contentMime))
{
    Console.WriteLine($"Detected by signature: {contentMime}");
}
```

`GetMimeTypeByContent` returns `MimeHelper.DefaultMimeType` when no known signature matches. `TryGetMimeTypeByContent` returns `false` in that case and still gives the fallback value through the `out` parameter.

Seekable streams are rewound to their original position after detection:

```csharp
using var stream = File.OpenRead("image.png");
stream.Position = 4;

var detected = MimeHelper.GetMimeTypeByContent(stream);

Console.WriteLine(detected);        // image/png
Console.WriteLine(stream.Position); // 4
```

Content detection is signature-based. It is useful for upload checks and mismatch detection, but it is not a full document parser, virus scanner, or guarantee that the entire file is structurally valid.

## Validating Uploads

For upload flows, compare the detected content type with the declared MIME type or with the file extension.

```csharp
using ManagedCode.MimeTypes;

using var stream = upload.OpenReadStream();

if (!MimeHelper.MatchesMimeTypeByContent(stream, upload.ContentType))
{
    throw new InvalidOperationException("The uploaded file content does not match its declared MIME type.");
}
```

If you trust the file name as the expected claim:

```csharp
using var stream = upload.OpenReadStream();

if (!MimeHelper.MatchesExtensionByContent(upload.FileName, stream))
{
    throw new InvalidOperationException("The uploaded file content does not match its extension.");
}
```

There is also a file-path overload:

```csharp
var ok = MimeHelper.MatchesExtensionByContent("/tmp/report.pdf");
```

## Registry Metadata

The package ships generated metadata for known MIME values. This is useful when you need to display registry details, audit data sources, inspect aliases, or use IANA magic-number metadata.

```csharp
using ManagedCode.MimeTypes;

if (MimeHelper.TryGetMimeTypeInfoByExtension("report.pdf", out var pdfInfo))
{
    Console.WriteLine(pdfInfo.Mime);              // application/pdf
    Console.WriteLine(pdfInfo.IsIanaRegistered);  // true
    Console.WriteLine(pdfInfo.TemplateUrl);       // https://www.iana.org/assignments/media-types/application/pdf
    Console.WriteLine(pdfInfo.MagicSignatures.FirstOrDefault()?.Hex); // 25 50 44 46 2D
}
```

Lookup directly by MIME:

```csharp
if (MimeHelper.TryGetMimeTypeInfo("application/json", out var jsonInfo))
{
    Console.WriteLine(jsonInfo.Source);
    Console.WriteLine(jsonInfo.PublishedSpecification);
}
```

List the bundled catalog:

```csharp
var registeredImages = MimeHelper.GetKnownMimeTypes()
    .Where(info => info.IsIanaRegistered && info.Mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
    .OrderBy(info => info.Mime)
    .ToList();
```

`MimeTypeInfo` includes:

| Property | Meaning |
| --- | --- |
| `Mime` | Canonical MIME value known to the catalog. |
| `Extensions` | Dot-prefixed extensions associated with the MIME value. |
| `IsIanaRegistered` | Whether the type is registered in the IANA media types registry. |
| `IsObsolete` / `PreferredMime` | Obsolescence state and replacement when available. |
| `Template` / `TemplateUrl` | IANA registration template path and URL. |
| `Source` | Source that supplied the metadata, such as `iana`, `apache`, `mime-db`, or `curated`. |
| `Registered` / `Updated` | Registry dates when available. |
| `IntendedUsage`, `EncodingConsiderations`, `PublishedSpecification`, `Applications` | Template fields parsed from registry data. |
| `DeprecatedAliases` / `References` | Alias and reference metadata. |
| `MagicSignatures` | Parseable fixed byte prefixes from registration templates. |

## Categories

`GetMimeCategory` maps a MIME value to a high-level `MimeTypeCategory`.

```csharp
var category = MimeHelper.GetMimeCategory("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
Console.WriteLine(category); // Spreadsheet

if (MimeHelper.IsArchive("application/zip"))
{
    Console.WriteLine("Archive upload");
}

if (MimeHelper.IsScript("application/x-powershell"))
{
    Console.WriteLine("Treat this as executable script content");
}
```

Available categories:

`Unknown`, `Video`, `Audio`, `Image`, `Document`, `Spreadsheet`, `Presentation`, `Pdf`, `Archive`, `Text`, `Json`, `Xml`, `Font`, `Model`, `Executable`, `Certificate`, `Calendar`, `Email`, `Script`, `Binary`, `Multipart`, `Message`.

Predicate helpers are available for the main categories: `IsVideo`, `IsAudio`, `IsImage`, `IsDocument`, `IsPdf`, `IsArchive`, `IsText`, `IsJson`, `IsXml`, `IsFont`, `IsModel`, `IsExecutable`, `IsCertificate`, `IsSpreadsheet`, `IsPresentation`, `IsCalendar`, `IsEmail`, `IsScript`, and `IsBinary`.

## Reverse Lookup

```csharp
var jpegExtensions = MimeHelper.GetExtensions("image/jpeg");

foreach (var extension in jpegExtensions)
{
    Console.WriteLine(extension); // .jpe, .jpeg, .jpg
}

if (MimeHelper.TryGetExtensions("application/pdf", out var pdfExtensions))
{
    Console.WriteLine(string.Join(", ", pdfExtensions)); // .pdf
}
```

## Runtime Registration

Applications can add or replace extension mappings at runtime.

```csharp
MimeHelper.RegisterMimeType("acme", "application/x-acme");

var custom = MimeHelper.GetMimeType("invoice.acme");
Console.WriteLine(custom); // application/x-acme

MimeHelper.UnregisterMimeType("acme");
```

Runtime registrations update extension lookup and reverse lookup, but they do not create generated registry metadata:

```csharp
MimeHelper.RegisterMimeType("internal", "application/x-company-internal");

Console.WriteLine(MimeHelper.GetMimeType("file.internal")); // application/x-company-internal
Console.WriteLine(MimeHelper.TryGetMimeTypeInfoByExtension("file.internal", out _)); // false

MimeHelper.UnregisterMimeType("internal");
```

The helper uses immutable dictionaries internally so lookup remains safe while mappings are updated.

## Default MIME Type

The default fallback is `application/octet-stream`.

```csharp
Console.WriteLine(MimeHelper.DefaultMimeType); // application/octet-stream

MimeHelper.SetDefaultMimeType(MimeHelper.JSON);
Console.WriteLine(MimeHelper.GetMimeType("unknown.extension")); // application/json

MimeHelper.SetDefaultMimeType(MimeHelper.BIN);
```

Use this carefully in shared applications because it changes process-wide behavior.

## DI-Friendly Adapter

For code that prefers an interface, use `MimeHelper.Instance`.

```csharp
using ManagedCode.MimeTypes;

public sealed class UploadClassifier(IMimeHelper mimeHelper)
{
    public string GetClaimedMime(string fileName)
    {
        return mimeHelper.GetMimeType(fileName);
    }
}

services.AddSingleton<IMimeHelper>(MimeHelper.Instance);
```

`IMimeHelper` covers common operations: extension lookup, content lookup, reverse lookup, runtime registration, unregistration, and categorisation. The static `MimeHelper` class exposes the full metadata and validation surface.

## Data Sources

The generated database is built from:

| Source | Purpose |
| --- | --- |
| IANA media types registry | Official media type registrations and templates. |
| Apache `mime.types` | Broad extension coverage used by common web server deployments. |
| mime-db | Compatibility gap-fill entries used by the JavaScript and web tooling ecosystem. |
| `curatedMimeTypes.json` | Project-maintained overrides for practical compatibility cases. |

Curated compatibility mappings and maintained runtime/server maps take precedence over raw IANA extension hints when they conflict. IANA remains the source for registration metadata.

## Refreshing The Catalog

The sync utility regenerates `mimeTypes.json` and `mimeTypes.metadata.json`.

```bash
dotnet run --project ManagedCode.MimeTypes.Sync
```

Provide custom inputs or outputs:

```bash
DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet run --project ManagedCode.MimeTypes.Sync -- \
    --iana-source https://www.iana.org/assignments/media-types/media-types.xml \
    --output ./artifacts/mimeTypes.json \
    --metadata-output ./artifacts/mimeTypes.metadata.json
```

Preserve an existing local map only when intentionally carrying compatibility data forward:

```bash
dotnet run --project ManagedCode.MimeTypes.Sync -- --preserve-existing --prefer-remote
```

Disable or replace the curated compatibility layer for policy experiments:

```bash
dotnet run --project ManagedCode.MimeTypes.Sync -- --no-curated
dotnet run --project ManagedCode.MimeTypes.Sync -- --curated-source ./my-curated-mime-types.json
```

Running the tool updates the JSON inputs. The source generator consumes those files on the next build and regenerates the helper constants, mappings, and metadata.

## Development

```bash
dotnet restore
dotnet build ManagedCode.MimeTypes.sln
dotnet test ManagedCode.MimeTypes.sln
```

The release workflow reads the package version from `Directory.Build.props`, packs the project, publishes to NuGet on `main`, and creates the GitHub release/tag when publishing succeeds.

## Contributing

Issues and pull requests are welcome. Please run `dotnet test ManagedCode.MimeTypes.sln` before sending changes. For catalog updates, prefer the sync utility so generated MIME data and metadata stay reproducible.
