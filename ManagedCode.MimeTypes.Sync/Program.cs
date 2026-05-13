using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

[assembly: InternalsVisibleTo("ManagedCode.MimeTypes.Tests")]

var exitCode = await MimeTypeSyncTool.RunAsync(args);
return exitCode;

internal static partial class MimeTypeSyncTool
{
    private const string DefaultIanaSource = "https://www.iana.org/assignments/media-types/media-types.xml";
    private const string IanaAssignmentsNamespace = "http://www.iana.org/assignments";
    private const string DefaultCuratedFileName = "curatedMimeTypes.json";
    private const string UserAgent = "ManagedCode.MimeTypes.Sync/2.0 (+https://github.com/managedcode/MimeTypes)";

    private static readonly string[] DefaultSupplementalSources =
    [
        "https://raw.githubusercontent.com/jshttp/mime-db/master/db.json",
        "https://raw.githubusercontent.com/apache/httpd/trunk/docs/conf/mime.types"
    ];

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = SyncOptions.Parse(args);
            using var client = CreateHttpClient();

            var sourceMappings = new List<SourceMapping>();
            var metadata = new Dictionary<string, MimeMetadata>(StringComparer.OrdinalIgnoreCase);
            IanaRegistry ianaRegistry = IanaRegistry.Empty;

            if (options.UseIana)
            {
                ianaRegistry = await LoadIanaRegistryAsync(client, options);

                foreach (var entry in ianaRegistry.Entries)
                {
                    metadata[entry.Mime] = entry.Clone();
                    foreach (var extension in entry.Extensions)
                    {
                        sourceMappings.Add(new SourceMapping(NormalizeExtension(extension), entry.Mime, MimeSourceKind.Iana));
                    }
                }
            }

            foreach (var source in options.SupplementalSources)
            {
                var kind = ClassifySupplementalSource(source);
                var raw = await LoadRawDataAsync(client, source);
                sourceMappings.AddRange(ParseSupplementalSource(source, raw, kind));
            }

            if (options.UseCurated)
            {
                foreach (var source in options.CuratedSources)
                {
                    var raw = await LoadRawDataAsync(client, source);
                    sourceMappings.AddRange(ParseSupplementalSource(source, raw, MimeSourceKind.Curated));
                }
            }

            var existing = options.PreserveExisting
                ? LoadExisting(options.OutputPath)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var merged = MergeMappings(sourceMappings, existing, options.PreferRemote);
            var mergedMetadata = BuildMergedMetadata(metadata, merged);

            WriteMimeMap(options.OutputPath, merged);
            WriteMetadata(options.MetadataOutputPath, mergedMetadata);
            WriteSummary(options, ianaRegistry, merged, mergedMetadata);

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    internal static IanaRegistry ParseIanaRegistry(byte[] data, string source)
    {
        var document = XDocument.Parse(Encoding.UTF8.GetString(data), LoadOptions.PreserveWhitespace);
        XNamespace ns = IanaAssignmentsNamespace;
        var root = document.Root ?? throw new InvalidOperationException("IANA registry XML has no root element.");
        var updated = root.Element(ns + "updated")?.Value.Trim();
        var entries = new Dictionary<string, MimeMetadata>(StringComparer.OrdinalIgnoreCase);

        foreach (var registry in root.Elements(ns + "registry"))
        {
            var topLevel = registry.Attribute("id")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(topLevel))
            {
                topLevel = registry.Element(ns + "title")?.Value.Trim();
            }

            if (string.IsNullOrWhiteSpace(topLevel))
            {
                continue;
            }

            foreach (var record in registry.Elements(ns + "record"))
            {
                var recordName = record.Element(ns + "name")?.Value.Trim();
                if (string.IsNullOrWhiteSpace(recordName))
                {
                    continue;
                }

                var templatePath = record.Elements(ns + "file")
                    .FirstOrDefault(static file => string.Equals(file.Attribute("type")?.Value, "template", StringComparison.OrdinalIgnoreCase))
                    ?.Value.Trim();

                var mime = !string.IsNullOrWhiteSpace(templatePath) && templatePath.Contains('/', StringComparison.Ordinal)
                    ? templatePath
                    : $"{topLevel}/{StripObsoleteMarker(recordName)}";

                mime = NormalizeMime(mime);
                if (string.IsNullOrWhiteSpace(mime))
                {
                    continue;
                }

                var entry = new MimeMetadata
                {
                    Mime = mime,
                    IsIanaRegistered = true,
                    IsObsolete = IsObsolete(recordName),
                    PreferredMime = ParsePreferredMime(recordName),
                    Template = templatePath,
                    TemplateUrl = ResolveTemplateUrl(source, templatePath),
                    Source = "iana",
                    Registered = record.Attribute("date")?.Value.Trim(),
                    Updated = record.Attribute("updated")?.Value.Trim() ?? updated,
                    References = record.Elements(ns + "xref")
                        .Select(FormatXref)
                        .Where(static value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                };

                entries[mime] = entry;
            }
        }

        return new IanaRegistry(updated, entries.Values.OrderBy(static entry => entry.Mime, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static void ApplyIanaTemplate(MimeMetadata metadata, string template, string? templateUrl)
    {
        metadata.TemplateUrl = templateUrl ?? metadata.TemplateUrl;

        var fields = ParseTemplateFields(template);
        if (fields.TryGetValue("file extension(s)", out var extensions))
        {
            foreach (var extension in ExtractExtensions(extensions))
            {
                metadata.AddExtension(extension);
            }
        }

        if (fields.TryGetValue("magic number(s)", out var magicText))
        {
            metadata.MagicSignatures = ParseMagicSignatures(magicText);
        }

        metadata.IntendedUsage = GetField(fields, "intended usage");
        metadata.EncodingConsiderations = GetField(fields, "encoding considerations");
        metadata.PublishedSpecification = GetField(fields, "published specification");
        metadata.Applications = GetField(fields, "applications that use this media type");

        if (fields.TryGetValue("deprecated alias names for this type", out var aliases) &&
            !string.Equals(NormalizeFreeText(aliases), "none", StringComparison.OrdinalIgnoreCase))
        {
            metadata.DeprecatedAliases = SplitLooseList(aliases);
        }
    }

    internal static IReadOnlyList<MagicSignatureMetadata> ParseMagicSignatures(string? raw)
    {
        var text = NormalizeFreeText(raw);
        if (string.IsNullOrWhiteSpace(text) || IsNoneValue(text))
        {
            return Array.Empty<MagicSignatureMetadata>();
        }

        var offsetQualified = RemoveRedundantPrefixSignatures(ParseOffsetQualifiedSignatures(raw ?? text, text))
            .ToArray();
        if (offsetQualified.Length > 0)
        {
            return offsetQualified;
        }

        var offset = ParseOffset(text);
        var parsed = RemoveRedundantPrefixSignatures(ParseQuotedAsciiSignatures(text, offset)
            .Concat(ParseHexSignatures(raw ?? text, text, offset))
            .DistinctBy(static signature => (signature.Hex, signature.Offset)))
            .ToArray();
        if (parsed.Length > 0)
        {
            return parsed;
        }

        return [new MagicSignatureMetadata(text, Array.Empty<int>(), null, offset)];
    }

    private static IEnumerable<MagicSignatureMetadata> RemoveRedundantPrefixSignatures(IEnumerable<MagicSignatureMetadata> signatures)
    {
        var kept = new List<MagicSignatureMetadata>();
        foreach (var signature in signatures.OrderBy(static value => value.Bytes.Length).ThenBy(static value => value.Hex, StringComparer.OrdinalIgnoreCase))
        {
            if (kept.Any(existing => existing.Offset == signature.Offset && IsPrefix(existing.Bytes, signature.Bytes)))
            {
                continue;
            }

            kept.Add(signature);
        }

        return kept
            .OrderBy(static value => value.Offset)
            .ThenBy(static value => value.Bytes.Length)
            .ThenBy(static value => value.Hex, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsPrefix(IReadOnlyList<int> prefix, IReadOnlyList<int> value)
    {
        if (prefix.Count == 0 || prefix.Count > value.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (prefix[i] != value[i])
            {
                return false;
            }
        }

        return true;
    }

    internal static IReadOnlyList<SourceMapping> ParseSupplementalSource(string source, byte[] data, MimeSourceKind kind)
    {
        var firstNonWhitespace = data.FirstOrDefault(static b => !char.IsWhiteSpace((char)b));
        return firstNonWhitespace is (byte)'{' or (byte)'['
            ? ParseJsonSource(source, data, kind)
            : ParseMimeTypesListing(source, data, kind);
    }

    internal static IReadOnlyDictionary<string, MergedMapping> MergeMappings(IEnumerable<SourceMapping> sourceMappings, IReadOnlyDictionary<string, string> existing, bool preferRemote)
    {
        var merged = new Dictionary<string, MergedMapping>(StringComparer.OrdinalIgnoreCase);
        var existingPriority = preferRemote ? MimeSourceKind.Existing.Priority() : MimeSourceKind.ExistingPreferred.Priority();

        foreach (var kvp in existing)
        {
            var extension = NormalizeExtension(kvp.Key);
            var mime = NormalizeMime(kvp.Value);
            if (extension.Length == 0 || mime.Length == 0)
            {
                continue;
            }

            merged[extension] = new MergedMapping(extension, mime, MimeSourceKind.Existing, existingPriority);
        }

        foreach (var mapping in sourceMappings)
        {
            var extension = NormalizeExtension(mapping.Extension);
            var mime = NormalizeMime(mapping.Mime);
            if (extension.Length == 0 || mime.Length == 0)
            {
                continue;
            }

            var priority = mapping.Kind.Priority();
            if (!merged.TryGetValue(extension, out var current) || priority >= current.Priority)
            {
                merged[extension] = new MergedMapping(extension, mime, mapping.Kind, priority);
            }
        }

        return merged;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.Timeout = TimeSpan.FromSeconds(60);
        return client;
    }

    private static async Task<IanaRegistry> LoadIanaRegistryAsync(HttpClient client, SyncOptions options)
    {
        var data = await LoadRawDataAsync(client, options.IanaSource);
        var registry = ParseIanaRegistry(data, options.IanaSource);

        if (options.SkipIanaTemplates)
        {
            return registry;
        }

        await FetchIanaTemplatesAsync(client, registry, options.TemplateConcurrency, options.IanaSource);
        return registry;
    }

    private static async Task FetchIanaTemplatesAsync(HttpClient client, IanaRegistry registry, int concurrency, string ianaSource)
    {
        var throttler = new SemaphoreSlim(Math.Max(1, concurrency));
        var errors = new ConcurrentBag<string>();

        var tasks = registry.Entries
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Template))
            .Select(async entry =>
            {
                await throttler.WaitAsync();
                try
                {
                    var templateUrl = ResolveTemplateUrl(ianaSource, entry.Template);
                    if (templateUrl == null)
                    {
                        return;
                    }

                    var templateBytes = await LoadRawDataWithRetryAsync(client, templateUrl);
                    var template = Encoding.UTF8.GetString(templateBytes);
                    ApplyIanaTemplate(entry, template, templateUrl);
                }
                catch (Exception ex)
                {
                    errors.Add($"{entry.Mime}: {ex.Message}");
                }
                finally
                {
                    throttler.Release();
                }
            });

        await Task.WhenAll(tasks);

        foreach (var error in errors.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).Take(20))
        {
            Console.WriteLine($"Warning: IANA template fetch failed for {error}");
        }

        if (errors.Count > 20)
        {
            Console.WriteLine($"Warning: {errors.Count - 20} additional IANA template fetch failures omitted.");
        }
    }

    private static async Task<byte[]> LoadRawDataWithRetryAsync(HttpClient client, string source)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                return await LoadRawDataAsync(client, source);
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }

        throw lastError ?? new InvalidOperationException($"Failed to load {source}.");
    }

    private static async Task<byte[]> LoadRawDataAsync(HttpClient client, string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return await client.GetByteArrayAsync(uri);
        }

        return await File.ReadAllBytesAsync(source);
    }

    private static IReadOnlyList<SourceMapping> ParseJsonSource(string source, byte[] data, MimeSourceKind kind)
    {
        using var document = JsonDocument.Parse(data);
        var mappings = new List<SourceMapping>();

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Object when property.Value.TryGetProperty("extensions", out var extensionsElement):
                        foreach (var extension in EnumerateJsonExtensions(extensionsElement))
                        {
                            AddMapping(mappings, extension, property.Name, kind);
                        }
                        break;
                    case JsonValueKind.String:
                        AddMapping(mappings, property.Name, property.Value.GetString(), kind);
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                AddMapping(mappings, item.GetString(), property.Name, kind);
                            }
                        }
                        break;
                }
            }
        }
        else if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object &&
                    element.TryGetProperty("extension", out var extensionProperty) &&
                    element.TryGetProperty("mime", out var mimeProperty))
                {
                    AddMapping(mappings, extensionProperty.GetString(), mimeProperty.GetString(), kind);
                }
            }
        }
        else
        {
            Console.WriteLine($"Warning: Unsupported JSON format from {source}.");
        }

        return mappings;
    }

    private static IEnumerable<string> EnumerateJsonExtensions(JsonElement extensionsElement)
    {
        if (extensionsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var extension in extensionsElement.EnumerateArray())
        {
            if (extension.ValueKind == JsonValueKind.String)
            {
                var value = extension.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    yield return value;
                }
            }
        }
    }

    private static IReadOnlyList<SourceMapping> ParseMimeTypesListing(string source, byte[] data, MimeSourceKind kind)
    {
        var mappings = new List<SourceMapping>();
        using var reader = new StreamReader(new MemoryStream(data), Encoding.UTF8, true);

        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            for (var i = 1; i < parts.Length; i++)
            {
                AddMapping(mappings, parts[i], parts[0], kind);
            }
        }

        if (mappings.Count == 0)
        {
            Console.WriteLine($"Warning: No MIME entries parsed from {source}.");
        }

        return mappings;
    }

    private static void AddMapping(List<SourceMapping> mappings, string? extension, string? mime, MimeSourceKind kind)
    {
        var normalizedExtension = NormalizeExtension(extension);
        var normalizedMime = NormalizeMime(mime);
        if (normalizedExtension.Length == 0 || normalizedMime.Length == 0)
        {
            return;
        }

        mappings.Add(new SourceMapping(normalizedExtension, normalizedMime, kind));
    }

    private static Dictionary<string, string> LoadExisting(string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var normalized = NormalizeExtension(property.Name);
            var value = NormalizeMime(property.Value.GetString());
            if (normalized.Length == 0 || value.Length == 0)
            {
                continue;
            }

            dictionary[normalized] = value;
        }

        return dictionary;
    }

    private static IReadOnlyDictionary<string, MimeMetadata> BuildMergedMetadata(Dictionary<string, MimeMetadata> metadata, IReadOnlyDictionary<string, MergedMapping> mergedMappings)
    {
        var result = metadata.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.Clone(), StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mergedMappings.Values)
        {
            if (!result.TryGetValue(mapping.Mime, out var entry))
            {
                entry = new MimeMetadata
                {
                    Mime = mapping.Mime,
                    IsIanaRegistered = false,
                    Source = mapping.Kind.SourceName()
                };
                result[mapping.Mime] = entry;
            }

            entry.AddExtension(mapping.Extension);
        }

        return result
            .Where(static kvp => !string.IsNullOrWhiteSpace(kvp.Key))
            .OrderBy(static kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value.NormalizeForOutput(), StringComparer.OrdinalIgnoreCase);
    }

    private static void WriteMimeMap(string outputPath, IReadOnlyDictionary<string, MergedMapping> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var stream = File.Create(outputPath);
        using var writer = CreateJsonWriter(stream);

        writer.WriteStartObject();
        foreach (var kvp in data.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteString(kvp.Key, kvp.Value.Mime);
        }

        writer.WriteEndObject();
    }

    private static void WriteMetadata(string outputPath, IReadOnlyDictionary<string, MimeMetadata> metadata)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var stream = File.Create(outputPath);
        using var writer = CreateJsonWriter(stream);

        JsonSerializer.Serialize(writer, metadata);
    }

    private static Utf8JsonWriter CreateJsonWriter(Stream stream)
    {
        return new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    private static void WriteSummary(SyncOptions options, IanaRegistry ianaRegistry, IReadOnlyDictionary<string, MergedMapping> mappings, IReadOnlyDictionary<string, MimeMetadata> metadata)
    {
        var ianaCount = metadata.Values.Count(static entry => entry.IsIanaRegistered);
        var supplementalCount = metadata.Count - ianaCount;
        var bySource = mappings.Values
            .GroupBy(static mapping => mapping.Kind.SourceName(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => $"{group.Key}: {group.Count().ToString("N0", CultureInfo.InvariantCulture)}");

        var summary = new StringBuilder();
        summary.AppendLine($"Updated {options.OutputPath} with {mappings.Count.ToString("N0", CultureInfo.InvariantCulture)} extension mappings.");
        summary.AppendLine($"Updated {options.MetadataOutputPath} with {metadata.Count.ToString("N0", CultureInfo.InvariantCulture)} MIME metadata records ({ianaCount.ToString("N0", CultureInfo.InvariantCulture)} IANA, {supplementalCount.ToString("N0", CultureInfo.InvariantCulture)} supplemental).");
        if (!string.IsNullOrWhiteSpace(ianaRegistry.Updated))
        {
            summary.AppendLine($"IANA registry updated: {ianaRegistry.Updated}");
        }

        summary.AppendLine("Mapping sources: " + string.Join(", ", bySource));

        Console.Write(summary.ToString());

        var githubSummary = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (!string.IsNullOrWhiteSpace(githubSummary))
        {
            File.AppendAllText(githubSummary, "## MIME database sync\n\n" + summary);
        }
    }

    private static Dictionary<string, string> ParseTemplateFields(string template)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var currentValue = new StringBuilder();

        using var reader = new StringReader(template.Replace("\r\n", "\n").Replace('\r', '\n'));
        while (reader.ReadLine() is { } line)
        {
            var match = TemplateFieldRegex().Match(line);
            if (match.Success)
            {
                FlushField(fields, currentKey, currentValue);
                currentKey = NormalizeTemplateFieldKey(match.Groups["name"].Value);
                currentValue.Clear();
                currentValue.Append(match.Groups["value"].Value.Trim());
                continue;
            }

            if (currentKey != null)
            {
                if (currentValue.Length > 0)
                {
                    currentValue.AppendLine();
                }

                currentValue.Append(line.Trim());
            }
        }

        FlushField(fields, currentKey, currentValue);
        return fields;
    }

    private static void FlushField(Dictionary<string, string> fields, string? currentKey, StringBuilder value)
    {
        if (currentKey == null)
        {
            return;
        }

        fields[currentKey] = value.ToString().Trim();
    }

    private static string? GetField(Dictionary<string, string> fields, string key)
    {
        return fields.TryGetValue(key, out var value) && !IsNoneValue(value)
            ? NormalizeFreeText(value)
            : null;
    }

    private static IReadOnlyList<string> ExtractExtensions(string raw)
    {
        var normalized = NormalizeFreeText(raw);
        if (IsNoneValue(normalized))
        {
            return Array.Empty<string>();
        }

        var extensions = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in DottedExtensionRegex().Matches(normalized))
        {
            var extension = NormalizeExtension(match.Groups["extension"].Value);
            if (extension.Length > 0)
            {
                extensions.Add("." + extension);
            }
        }

        if (extensions.Count > 0)
        {
            return extensions.ToArray();
        }

        if (!LooseExtensionListRegex().IsMatch(normalized))
        {
            return Array.Empty<string>();
        }

        foreach (var token in normalized.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var extension = NormalizeExtension(token.Trim('(', ')', '[', ']', '"', '\''));
            if (extension.Length is > 0 and <= 32 && ExtensionTokenRegex().IsMatch(extension))
            {
                extensions.Add("." + extension);
            }
        }

        return extensions.ToArray();
    }

    private static string[] SplitLooseList(string raw)
    {
        return raw.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeFreeText)
            .Where(static value => !IsNoneValue(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<MagicSignatureMetadata> ParseOffsetQualifiedSignatures(string raw, string text)
    {
        foreach (Match match in OffsetQualifiedSignatureRegex().Matches(raw))
        {
            if (!int.TryParse(match.Groups["offset"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset))
            {
                continue;
            }

            if (match.Groups["ascii"].Success)
            {
                var value = match.Groups["ascii"].Value;
                if (!IsAsciiSignatureValue(value))
                {
                    continue;
                }

                var asciiBytes = Encoding.ASCII.GetBytes(value);
                yield return new MagicSignatureMetadata(text, asciiBytes.Select(static value => (int)value).ToArray(), ToHex(asciiBytes), offset);
                continue;
            }

            if (match.Groups["hex"].Success && TryParseCompactHex(match.Groups["hex"].Value, out var hexBytes))
            {
                yield return new MagicSignatureMetadata(text, hexBytes.Select(static value => (int)value).ToArray(), ToHex(hexBytes), offset);
            }
        }
    }

    private static IEnumerable<MagicSignatureMetadata> ParseQuotedAsciiSignatures(string text, int offset)
    {
        foreach (Match match in QuotedStringRegex().Matches(text))
        {
            var value = match.Groups["value"].Value;
            if (!IsAsciiMagicCandidate(value, text, match.Index, match.Length))
            {
                continue;
            }

            var bytes = Encoding.ASCII.GetBytes(value);
            yield return new MagicSignatureMetadata(text, bytes.Select(static value => (int)value).ToArray(), ToHex(bytes), offset);
        }
    }

    private static IEnumerable<MagicSignatureMetadata> ParseHexSignatures(string raw, string text, int offset)
    {
        var candidates = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(['\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (candidates.Length == 0 || HexByteListRegex().IsMatch(text))
        {
            candidates = [text];
        }

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeFreeText(candidate);
            var bytes = ParseExplicitHexBytes(normalized);
            if (bytes.Length < 2)
            {
                continue;
            }

            yield return new MagicSignatureMetadata(text, bytes.Select(static value => (int)value).ToArray(), ToHex(bytes), offset);
        }
    }

    private static bool IsAsciiMagicCandidate(string value, string text, int matchIndex, int matchLength)
    {
        if (!IsAsciiSignatureValue(value))
        {
            return false;
        }

        if (value.Any(static c => c < 0x20 || c > 0x7E))
        {
            return false;
        }

        var normalizedValue = NormalizeFreeText(value);
        if (normalizedValue.Equals("magic number", StringComparison.OrdinalIgnoreCase) ||
            normalizedValue.Equals("file", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalizedValue.Any(static c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c)))
        {
            return true;
        }

        if (normalizedValue.Any(char.IsDigit))
        {
            return true;
        }

        if (normalizedValue.Equals(normalizedValue.ToUpperInvariant(), StringComparison.Ordinal) && normalizedValue.Any(char.IsLetter))
        {
            return true;
        }

        var contextStart = Math.Max(0, matchIndex - 80);
        var contextLength = Math.Min(text.Length - contextStart, matchLength + 160);
        var context = text.Substring(contextStart, contextLength);
        return normalizedValue.Length >= 4 &&
            char.IsUpper(normalizedValue[0]) &&
            (context.Contains("leading", StringComparison.OrdinalIgnoreCase) ||
             context.Contains("start", StringComparison.OrdinalIgnoreCase) ||
             context.Contains("begin", StringComparison.OrdinalIgnoreCase)) &&
            !context.Contains("command", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAsciiSignatureValue(string value)
    {
        return value.Length is >= 2 and <= 64 &&
            value.Any(static c => !char.IsWhiteSpace(c)) &&
            value.All(static c => c is >= (char)0x20 and <= (char)0x7E);
    }

    private static byte[] ParseExplicitHexBytes(string value)
    {
        if (HexByteListRegex().IsMatch(value))
        {
            return HexByteRegex().Matches(value)
                .Select(static match => byte.Parse(match.Groups["byte"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
                .ToArray();
        }

        return ExplicitHexRegex().Matches(value)
            .SelectMany(static match => TryParseCompactHex(match.Groups["hex"].Value, out var bytes) ? bytes : [])
            .ToArray();
    }

    private static bool TryParseCompactHex(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length < 2 || value.Length % 2 != 0 || !value.All(Uri.IsHexDigit))
        {
            return false;
        }

        bytes = Enumerable.Range(0, value.Length / 2)
            .Select(index => byte.Parse(value.Substring(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();
        return true;
    }

    private static int ParseOffset(string text)
    {
        var match = OffsetRegex().Match(text);
        return match.Success && int.TryParse(match.Groups["offset"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
            ? offset
            : 0;
    }

    private static string ToHex(IEnumerable<byte> bytes)
    {
        return string.Join(" ", bytes.Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.Trim().Trim('.').Trim().TrimEnd('.', ',', ';', ':').ToLowerInvariant();
    }

    private static string NormalizeMime(string? mime)
    {
        if (string.IsNullOrWhiteSpace(mime))
        {
            return string.Empty;
        }

        return mime.Trim();
    }

    private static string NormalizeFreeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return WhitespaceRegex().Replace(value.Trim(), " ");
    }

    private static string NormalizeTemplateFieldKey(string value)
    {
        return WhitespaceRegex().Replace(value.Trim().TrimEnd(':'), " ").ToLowerInvariant();
    }

    private static bool IsNoneValue(string value)
    {
        var normalized = NormalizeFreeText(value).Trim('.', '-');
        return normalized.Length == 0 ||
            normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("not applicable", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("no", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripObsoleteMarker(string value)
    {
        var markerIndex = value.IndexOf(" (OBSOLETED", StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0 ? value : value[..markerIndex];
    }

    private static bool IsObsolete(string value)
    {
        return value.Contains("OBSOLETED", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParsePreferredMime(string value)
    {
        var match = PreferredMimeRegex().Match(value);
        return match.Success ? match.Groups["mime"].Value.TrimEnd(')', '.', ',') : null;
    }

    private static string? ResolveTemplateUrl(string source, string? templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return null;
        }

        if (Uri.TryCreate(templatePath, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var sourceUri))
        {
            return null;
        }

        var baseUri = sourceUri.ToString().EndsWith("/", StringComparison.Ordinal)
            ? sourceUri
            : new Uri(sourceUri, ".");

        return new Uri(baseUri, templatePath).ToString();
    }

    private static string FormatXref(XElement xref)
    {
        var type = xref.Attribute("type")?.Value;
        var data = xref.Attribute("data")?.Value;
        var text = NormalizeFreeText(xref.Value);

        if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(data))
        {
            return $"{type}:{data}";
        }

        return !string.IsNullOrWhiteSpace(text) ? text : string.Empty;
    }

    private static MimeSourceKind ClassifySupplementalSource(string source)
    {
        if (source.Contains("jshttp/mime-db", StringComparison.OrdinalIgnoreCase) ||
            source.EndsWith("db.json", StringComparison.OrdinalIgnoreCase))
        {
            return MimeSourceKind.MimeDb;
        }

        if (source.Contains("apache", StringComparison.OrdinalIgnoreCase) ||
            source.EndsWith("mime.types", StringComparison.OrdinalIgnoreCase))
        {
            return MimeSourceKind.Apache;
        }

        return MimeSourceKind.Custom;
    }

    private static string ResolveDefaultCuratedSource()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, DefaultCuratedFileName),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", DefaultCuratedFileName)),
            Path.Combine(Directory.GetCurrentDirectory(), "ManagedCode.MimeTypes.Sync", DefaultCuratedFileName),
            Path.Combine(Directory.GetCurrentDirectory(), DefaultCuratedFileName)
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    [GeneratedRegex(@"^\s*(?:\d+\.\s*)?(?<name>[A-Za-z][A-Za-z0-9 /&().+\-]*(?:\(s\))?)\s*:\s*(?<value>.*)$", RegexOptions.Compiled)]
    private static partial Regex TemplateFieldRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])\.(?<extension>[A-Za-z0-9][A-Za-z0-9.+_-]{0,63})", RegexOptions.Compiled)]
    private static partial Regex DottedExtensionRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.+_-]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ExtensionTokenRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]*(?:\s*[,;]\s*[A-Za-z0-9][A-Za-z0-9.+_-]*)*$", RegexOptions.Compiled)]
    private static partial Regex LooseExtensionListRegex();

    [GeneratedRegex(@"(?<quote>[""'])(?<value>[^""']{2,64})\k<quote>", RegexOptions.Compiled)]
    private static partial Regex QuotedStringRegex();

    [GeneratedRegex(@"(?:0x)?(?<byte>[0-9A-Fa-f]{2})(?![0-9A-Fa-f])", RegexOptions.Compiled)]
    private static partial Regex HexByteRegex();

    [GeneratedRegex(@"^\s*(?:[0-9A-Fa-f]{2}\s*){2,}$", RegexOptions.Compiled)]
    private static partial Regex HexByteListRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])0x(?<hex>[0-9A-Fa-f]{2,})(?![A-Za-z0-9])", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitHexRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?<offset>\d+)\s*:\s*(?:(?<quote>[""'])(?<ascii>[^""']{2,64})\k<quote>|0x(?<hex>[0-9A-Fa-f]{2,})(?![A-Za-z0-9]))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex OffsetQualifiedSignatureRegex();

    [GeneratedRegex(@"offset\s+(?<offset>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex OffsetRegex();

    [GeneratedRegex(@"in favor of (?<mime>[a-z0-9.+_-]+/[a-z0-9.+_-]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PreferredMimeRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

    internal sealed record SyncOptions(
        string IanaSource,
        IReadOnlyList<string> SupplementalSources,
        IReadOnlyList<string> CuratedSources,
        string OutputPath,
        string MetadataOutputPath,
        bool PreferRemote,
        bool UseIana,
        bool UseCurated,
        bool SkipIanaTemplates,
        bool PreserveExisting,
        int TemplateConcurrency)
    {
        public static SyncOptions Parse(string[] args)
        {
            var ianaSource = DefaultIanaSource;
            var supplementalSources = new List<string>(DefaultSupplementalSources);
            var curatedSources = new List<string> { ResolveDefaultCuratedSource() };
            string? output = null;
            string? metadataOutput = null;
            bool preferRemote = false;
            bool useIana = true;
            bool useCurated = true;
            bool skipIanaTemplates = false;
            bool preserveExisting = false;
            var templateConcurrency = 8;
            var customSupplementalSources = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--iana-source" when i + 1 < args.Length:
                        ianaSource = args[++i];
                        break;
                    case "--no-iana":
                        useIana = false;
                        break;
                    case "--no-curated":
                        useCurated = false;
                        curatedSources.Clear();
                        break;
                    case "--skip-iana-templates":
                        skipIanaTemplates = true;
                        break;
                    case "--template-concurrency" when i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var concurrency):
                        templateConcurrency = Math.Clamp(concurrency, 1, 32);
                        break;
                    case "--source" when i + 1 < args.Length:
                        if (!customSupplementalSources)
                        {
                            supplementalSources.Clear();
                            customSupplementalSources = true;
                        }

                        AddSources(supplementalSources, args[++i]);
                        break;
                    case "--add-source" when i + 1 < args.Length:
                        AddSources(supplementalSources, args[++i]);
                        break;
                    case "--curated-source" when i + 1 < args.Length:
                        curatedSources.Clear();
                        AddSources(curatedSources, args[++i]);
                        useCurated = true;
                        break;
                    case "--add-curated-source" when i + 1 < args.Length:
                        AddSources(curatedSources, args[++i]);
                        useCurated = true;
                        break;
                    case "--reset-sources":
                        supplementalSources.Clear();
                        customSupplementalSources = true;
                        break;
                    case "--output" when i + 1 < args.Length:
                        output = args[++i];
                        break;
                    case "--metadata-output" when i + 1 < args.Length:
                        metadataOutput = args[++i];
                        break;
                    case "--prefer-remote":
                        preferRemote = true;
                        break;
                    case "--preserve-existing":
                        preserveExisting = true;
                        break;
                }
            }

            output ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ManagedCode.MimeTypes", "mimeTypes.json"));
            metadataOutput ??= Path.Combine(Path.GetDirectoryName(output)!, "mimeTypes.metadata.json");

            return new SyncOptions(ianaSource, supplementalSources, curatedSources, output, metadataOutput, preferRemote, useIana, useCurated, skipIanaTemplates, preserveExisting, templateConcurrency);
        }

        private static void AddSources(List<string> sources, string value)
        {
            foreach (var source in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                sources.Add(source);
            }
        }
    }
}

internal enum MimeSourceKind
{
    Existing,
    MimeDb,
    Apache,
    Custom,
    Iana,
    ExistingPreferred,
    Curated
}

internal static class MimeSourceKindExtensions
{
    public static int Priority(this MimeSourceKind kind)
    {
        return kind switch
        {
            MimeSourceKind.Existing => 0,
            MimeSourceKind.Iana => 5,
            MimeSourceKind.MimeDb => 15,
            MimeSourceKind.Apache => 20,
            MimeSourceKind.Custom => 30,
            MimeSourceKind.ExistingPreferred => 35,
            MimeSourceKind.Curated => 40,
            _ => 0
        };
    }

    public static string SourceName(this MimeSourceKind kind)
    {
        return kind switch
        {
            MimeSourceKind.MimeDb => "mime-db",
            MimeSourceKind.Apache => "apache",
            MimeSourceKind.Custom => "custom",
            MimeSourceKind.Iana => "iana",
            MimeSourceKind.ExistingPreferred => "existing",
            MimeSourceKind.Curated => "curated",
            _ => "existing"
        };
    }
}

internal sealed record SourceMapping(string Extension, string Mime, MimeSourceKind Kind);

internal sealed record MergedMapping(string Extension, string Mime, MimeSourceKind Kind, int Priority);

internal sealed record IanaRegistry(string? Updated, IReadOnlyList<MimeMetadata> Entries)
{
    public static IanaRegistry Empty { get; } = new(null, Array.Empty<MimeMetadata>());
}

internal sealed class MimeMetadata
{
    private readonly SortedSet<string> _extensions = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("mime")]
    public string Mime { get; init; } = string.Empty;

    [JsonPropertyName("extensions")]
    public string[] Extensions
    {
        get => _extensions.ToArray();
        init
        {
            _extensions.Clear();
            foreach (var extension in value ?? Array.Empty<string>())
            {
                AddExtension(extension);
            }
        }
    }

    [JsonPropertyName("isIanaRegistered")]
    public bool IsIanaRegistered { get; init; }

    [JsonPropertyName("isObsolete")]
    public bool IsObsolete { get; init; }

    [JsonPropertyName("preferredMime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreferredMime { get; init; }

    [JsonPropertyName("template")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Template { get; init; }

    [JsonPropertyName("templateUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateUrl { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("registered")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Registered { get; init; }

    [JsonPropertyName("updated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Updated { get; init; }

    [JsonPropertyName("intendedUsage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntendedUsage { get; set; }

    [JsonPropertyName("encodingConsiderations")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EncodingConsiderations { get; set; }

    [JsonPropertyName("publishedSpecification")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PublishedSpecification { get; set; }

    [JsonPropertyName("applications")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Applications { get; set; }

    [JsonPropertyName("deprecatedAliases")]
    public string[] DeprecatedAliases { get; set; } = [];

    [JsonPropertyName("references")]
    public string[] References { get; init; } = [];

    [JsonPropertyName("magicSignatures")]
    public IReadOnlyList<MagicSignatureMetadata> MagicSignatures { get; set; } = Array.Empty<MagicSignatureMetadata>();

    public void AddExtension(string extension)
    {
        var normalized = extension.Trim().Trim('.').Trim();
        if (normalized.Length > 0)
        {
            _extensions.Add("." + normalized.ToLowerInvariant());
        }
    }

    public MimeMetadata Clone()
    {
        return new MimeMetadata
        {
            Mime = Mime,
            Extensions = Extensions,
            IsIanaRegistered = IsIanaRegistered,
            IsObsolete = IsObsolete,
            PreferredMime = PreferredMime,
            Template = Template,
            TemplateUrl = TemplateUrl,
            Source = Source,
            Registered = Registered,
            Updated = Updated,
            IntendedUsage = IntendedUsage,
            EncodingConsiderations = EncodingConsiderations,
            PublishedSpecification = PublishedSpecification,
            Applications = Applications,
            DeprecatedAliases = DeprecatedAliases,
            References = References,
            MagicSignatures = MagicSignatures
        };
    }

    public MimeMetadata NormalizeForOutput()
    {
        return new MimeMetadata
        {
            Mime = Mime,
            Extensions = Extensions,
            IsIanaRegistered = IsIanaRegistered,
            IsObsolete = IsObsolete,
            PreferredMime = PreferredMime,
            Template = Template,
            TemplateUrl = TemplateUrl,
            Source = Source,
            Registered = Registered,
            Updated = Updated,
            IntendedUsage = IntendedUsage,
            EncodingConsiderations = EncodingConsiderations,
            PublishedSpecification = PublishedSpecification,
            Applications = Applications,
            DeprecatedAliases = DeprecatedAliases.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            References = References.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            MagicSignatures = MagicSignatures
        };
    }
}

internal sealed record MagicSignatureMetadata(
    [property: JsonPropertyName("raw")] string Raw,
    [property: JsonPropertyName("bytes")] int[] Bytes,
    [property: JsonPropertyName("hex")] string? Hex,
    [property: JsonPropertyName("offset")] int Offset);
