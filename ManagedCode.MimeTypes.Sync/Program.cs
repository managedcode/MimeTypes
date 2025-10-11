using System.Net.Http;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

var exitCode = await MimeTypeSyncTool.RunAsync(args);
return exitCode;

internal static class MimeTypeSyncTool
{
    private static readonly string[] DefaultSources =
    {
        "https://raw.githubusercontent.com/jshttp/mime-db/master/db.json",
        "https://raw.githubusercontent.com/apache/httpd/trunk/docs/conf/mime.types"
    };

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = SyncOptions.Parse(args);
            var remoteData = await LoadRemoteAsync(options.Sources);
            var existing = LoadExisting(options.OutputPath);
            var merged = Merge(remoteData, existing, options);

            WriteOutput(options.OutputPath, merged);

            Console.WriteLine($"Updated {options.OutputPath} with {merged.Count.ToString("N0", CultureInfo.InvariantCulture)} entries (remote: {remoteData.Count.ToString("N0", CultureInfo.InvariantCulture)}, existing: {existing.Count.ToString("N0", CultureInfo.InvariantCulture)}).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task<Dictionary<string, string>> LoadRemoteAsync(IReadOnlyList<string> sources)
    {
        if (sources.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ManagedCode.MimeTypes.Sync/1.0");

        var aggregate = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            var data = await LoadRawDataAsync(client, source);
            var parsed = ParseRemoteData(source, data);

            foreach (var kvp in parsed)
            {
                aggregate[kvp.Key] = kvp.Value;
            }
        }
        return aggregate;
    }

    private static async Task<byte[]> LoadRawDataAsync(HttpClient client, string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return await client.GetByteArrayAsync(uri);
        }

        return await File.ReadAllBytesAsync(source);
    }

    private static Dictionary<string, string> ParseRemoteData(string source, byte[] data)
    {
        var firstNonWhitespace = data.FirstOrDefault(static b => !char.IsWhiteSpace((char)b));
        if (firstNonWhitespace == '{' || firstNonWhitespace == '[')
        {
            return ParseJsonSource(source, data);
        }

        return ParseMimeTypesListing(source, data);
    }

    private static Dictionary<string, string> ParseJsonSource(string source, byte[] data)
    {
        using var document = JsonDocument.Parse(data);
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Object when property.Value.TryGetProperty("extensions", out var extensionsElement):
                        AddExtensions(dictionary, property.Name, extensionsElement);
                        break;
                    case JsonValueKind.String:
                        AddExtension(dictionary, property.Name, property.Value.GetString());
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String)
                            {
                                AddExtension(dictionary, item.GetString(), property.Name);
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
                    AddExtension(dictionary, extensionProperty.GetString(), mimeProperty.GetString());
                }
            }
        }
        else
        {
            Console.WriteLine($"Warning: Unsupported JSON format from {source}." );
        }

        return dictionary;
    }

    private static Dictionary<string, string> ParseMimeTypesListing(string source, byte[] data)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StreamReader(new MemoryStream(data), Encoding.UTF8, true);

        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split(' ', '\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var mime = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                AddExtension(dictionary, parts[i], mime);
            }
        }

        if (dictionary.Count == 0)
        {
            Console.WriteLine($"Warning: No MIME entries parsed from {source}." );
        }

        return dictionary;
    }

    private static void AddExtensions(Dictionary<string, string> dictionary, string mime, JsonElement extensionsElement)
    {
        foreach (var extension in extensionsElement.EnumerateArray())
        {
            if (extension.ValueKind == JsonValueKind.String)
            {
                AddExtension(dictionary, extension.GetString(), mime);
            }
        }
    }

    private static void AddExtension(Dictionary<string, string> dictionary, string? extension, string? mime)
    {
        var normalized = NormalizeExtension(extension);
        if (string.IsNullOrEmpty(normalized) || string.IsNullOrWhiteSpace(mime))
        {
            return;
        }

        dictionary[normalized] = mime!;
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
            var value = property.Value.GetString();
            if (string.IsNullOrEmpty(normalized) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            dictionary[normalized] = value;
        }

        return dictionary;
    }

    private static Dictionary<string, string> Merge(Dictionary<string, string> remote, Dictionary<string, string> existing, SyncOptions options)
    {
        var result = new Dictionary<string, string>(remote, StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in existing)
        {
            if (options.PreferRemote)
            {
                result.TryAdd(kvp.Key, kvp.Value);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }

        foreach (var kvp in CustomMappings())
        {
            result[kvp.Key] = kvp.Value;
        }

        return result;
    }

    private static IEnumerable<KeyValuePair<string, string>> CustomMappings()
    {
        yield return new KeyValuePair<string, string>("tar.gz", "application/gzip");
        yield return new KeyValuePair<string, string>("tar.bz2", "application/x-bzip2");
        yield return new KeyValuePair<string, string>("tar.xz", "application/x-xz");
        yield return new KeyValuePair<string, string>("tar.zst", "application/zstd");
        yield return new KeyValuePair<string, string>("d.ts", "application/typescript");
        yield return new KeyValuePair<string, string>("cjs", "application/node");
        yield return new KeyValuePair<string, string>("mjs", "text/javascript");
        yield return new KeyValuePair<string, string>("wasm", "application/wasm");
        yield return new KeyValuePair<string, string>("heic", "image/heic");
        yield return new KeyValuePair<string, string>("heif", "image/heif");
        yield return new KeyValuePair<string, string>("ics", "text/calendar");
        yield return new KeyValuePair<string, string>("ps1", "application/x-powershell");
        yield return new KeyValuePair<string, string>("appx", "application/vnd.ms-appx");
    }

    private static void WriteOutput(string outputPath, Dictionary<string, string> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var stream = File.Create(outputPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        writer.WriteStartObject();
        foreach (var kvp in data.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            writer.WriteString(kvp.Key, kvp.Value);
        }

        writer.WriteEndObject();
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        return extension.Trim().Trim('.').ToLowerInvariant();
    }

    private sealed record SyncOptions(IReadOnlyList<string> Sources, string OutputPath, bool PreferRemote)
    {
        public static SyncOptions Parse(string[] args)
        {
            var sources = new List<string>(DefaultSources);
            string? output = null;
            bool preferRemote = false;
            var customSources = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--source" when i + 1 < args.Length:
                        if (!customSources)
                        {
                            sources.Clear();
                            customSources = true;
                        }

                        foreach (var value in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            sources.Add(value);
                        }
                        break;
                    case "--add-source" when i + 1 < args.Length:
                        foreach (var value in args[++i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        {
                            sources.Add(value);
                        }
                        break;
                    case "--reset-sources":
                        sources.Clear();
                        customSources = true;
                        break;
                    case "--output" when i + 1 < args.Length:
                        output = args[++i];
                        break;
                    case "--prefer-remote":
                        preferRemote = true;
                        break;
                }
            }

            output ??= Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ManagedCode.MimeTypes", "mimeTypes.json"));

            return new SyncOptions(sources, output, preferRemote);
        }
    }
}
