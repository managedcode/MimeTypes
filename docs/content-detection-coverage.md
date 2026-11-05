# Content detection verification for issue #23

The catalog contains 1771 extension mappings. All mappings are covered by the existing forward/reverse lookup and generated metadata tests. Content detection needs separate evidence: an extension mapping does not imply that bytes uniquely identify a format.

## Format inventory and acceptance matrix

| Family | Formats to verify | Detection evidence |
| --- | --- | --- |
| Office OPC | `docx`, `docm`, `dotx`, `dotm`, `xlsx`, `xlsm`, `xltx`, `xltm`, `xlam`, `xlsb`, `pptx`, `pptm`, `potx`, `potm`, `ppsx`, `ppsm`, `ppam`, `sldx`, `sldm`, `thmx` | Container metadata and exact subtype |
| Visio OPC | `vsdx`, `vsdm`, `vssx`, `vssm`, `vstx`, `vstm` | Container metadata and exact subtype |
| Other OPC | `xps`, `oxps`, `3mf` | Container metadata and exact subtype |
| OpenDocument ZIP | `odb`, `odc`, `odf`, `odft`, `odg`, `odi`, `odm`, `odp`, `ods`, `odt`, `otc`, `otg`, `oth`, `oti`, `otm`, `otp`, `ots`, `ott` | Container metadata and exact subtype |
| OpenOffice ZIP | `stc`, `std`, `sti`, `stw`, `sxc`, `sxd`, `sxg`, `sxi`, `sxm`, `sxw` | Container metadata and exact subtype |
| Other ZIP | `epub`, `apk`, `jar`, `war`, `ear` | Container metadata and exact subtype |
| Legacy compound Office | `doc`, `dot`, `xls`, `xlt`, `xla`, `ppt`, `pot`, `pps`, `vsd`, `vss`, `vst` | Root directory stream names; generic fallback for unknown/encrypted compound files |
| RIFF | `webp`, `wav`, `avi` | Container metadata and exact subtype |
| ISO BMFF | `mp4`, `mov`, `m4a`, `m4v`, `3gp`, `3g2`, `avif`, `heic`, `heif`, `heics`, `heifs` | Container metadata and exact subtype |
| Ogg | `ogg`, `oga`, `ogv`, `ogx`, `opus` | Container metadata and exact subtype |
| Built-in independent signatures | `pdf`, `jpg`, `png`, `gif`, `bmp`, `tif`, `ico`, `psd`, `gz`, `bz2`, `rar`, `7z`, `xz`, `flac`, `mp3`, `sqlite`, `ps`, `elf`, `exe`, `rtf`, `torrent` | Complete fixed signatures; truncated and near-match negative cases |

## Required cases

- ZIP/OPC: compressed and stored metadata, metadata beyond the header, arbitrary entry order and main-part location, Transitional/Strict relationships, defaults/overrides, macro/template/slideshow/slide/add-in distinctions.
- Stream behavior: seekable and non-seekable streams, short reads, nonzero initial position, leave-open behavior, file-path/adapter/validation APIs.
- Negative cases: plain ZIP with Office names or marker text, missing targets, external relationships, duplicate/conflicting metadata, malformed/truncated archives and XML, oversized metadata/central directories, unsupported compression.
- Resource bounds: bounded ZIP/compound reads and decompression; unchanged small-header path for independent signatures.
- Shared binary containers: complete RIFF/FourCC values, ISO BMFF major and compatible brands, Ogg codec headers, compound directory/FAT validation and generic fallback when subtype evidence is absent.

## Scope boundaries

Identical bytes can have different file-name extensions (for example ZIP-derived comic archives, Java archive variants, and legacy Office templates). Detection reports the MIME type justified by the payload. Unknown or ambiguous subtypes keep a generic fallback. Filename guesses and extension-specific assertions must not replace content evidence.

The complete per-extension audit inventory is generated at `artifacts/issue-23/format-inventory.csv`; the executable regressions are the acceptance evidence, and this matrix is updated to match the delivered scope.

ELF is audited as a binary signature family; the catalog currently has no `.elf` mapping. The explicit matrix therefore covers 113 format/extension names, including that signature-only family.

## Validation for 10.1.11

- Scoped `dotnet format whitespace --verify-no-changes` passed.
- `dotnet build ManagedCode.MimeTypes.sln -c Release --no-restore` passed with zero warnings/errors.
- `dotnet test ManagedCode.MimeTypes.sln -c Release --no-build` passed all 546 tests, including all 23 performance checks.
- `dotnet pack ManagedCode.MimeTypes.sln -c Release --no-build` passed package validation.
- An independent consumer correctly classified 33 real public fixtures from the Apache Tika test resources, covering Office macro/template/binary packages, all six Visio ZIP variants, legacy compound Office, XPS, 3MF, ODF, EPUB, APK, and JAR. Fixtures and source paths are retained under `artifacts/issue-23/` locally; no document parsing or macro execution is performed.

Generated ZIP/CFB/media fixtures exercise the remaining subtype and malformed-input cases without network dependencies in the test suite. The existing cache tests check every catalog mapping and every generated metadata record.
