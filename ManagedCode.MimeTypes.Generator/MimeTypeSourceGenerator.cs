using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;

namespace ManagedCode.MimeTypes.Generator;

/// <summary>
/// Emits source that bootstraps MIME mappings and exposes typed constants for each extension.
/// </summary>
[Generator]
public class MimeTypeSourceGenerator : ISourceGenerator
{
    private static readonly DiagnosticDescriptor MimeTypesLoadedDiagnostic = new(
        "MIME002",
        "MimeTypes loaded",
        "Successfully loaded {0} mime types",
        "MimeTypes",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }
#endif
    }

    /// <inheritdoc />
    public void Execute(GeneratorExecutionContext context)
    {
        try
        {
            // Find the mimeTypes.json file
            var mimeTypesPath = GetMimeTypesPath(context);
            
            if (!File.Exists(mimeTypesPath))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "MIME001",
                        "MimeTypes.json not found",
                        "Could not find mimeTypes.json at {0}",
                        "MimeTypes",
                        DiagnosticSeverity.Error,
                        true),
                    Location.None,
                    mimeTypesPath));
                return;
            }

            var json = File.ReadAllBytes(mimeTypesPath);
            using var document = JsonDocument.Parse(json);

            StringBuilder defineDictionaryBuilder = new();
            StringBuilder metadataBuilder = new();
            StringBuilder propertyBuilder = new();
            Dictionary<string, string> types = new(StringComparer.OrdinalIgnoreCase);

            foreach (var item in document.RootElement.EnumerateObject())
            {
                var extension = item.Name.Trim();
                var mimeValue = item.Value.GetString()?.Trim() ?? string.Empty;

                if (extension.Length == 0 || mimeValue.Length == 0)
                {
                    continue;
                }

                defineDictionaryBuilder.AppendLine($"RegisterMimeTypeInternal(\"{Escape(extension)}\", \"{Escape(mimeValue)}\");");
                types[ParseKey(extension)] = mimeValue;
            }

            var metadataPath = GetMetadataPath(mimeTypesPath, context);
            if (File.Exists(metadataPath))
            {
                using var metadataDocument = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
                foreach (var item in metadataDocument.RootElement.EnumerateObject())
                {
                    var initializer = BuildMimeTypeInfoInitializer(item.Name, item.Value);
                    if (initializer.Length > 0)
                    {
                        metadataBuilder.AppendLine($"RegisterMimeTypeInfoInternal({initializer});");
                    }
                }
            }

            foreach (var item in types)
            {
                propertyBuilder.AppendLine($"public static string {item.Key} => \"{Escape(item.Value)}\";");
            }

            context.ReportDiagnostic(Diagnostic.Create(MimeTypesLoadedDiagnostic, Location.None, types.Count));
            
            context.AddSource("MimeHelper.Properties.cs", SourceText.From(@$"
using System;
using System.Collections.Immutable;

namespace ManagedCode.MimeTypes
{{
public static partial class MimeHelper
{{
static partial void Init()
{{
{defineDictionaryBuilder}
{metadataBuilder}
}}
{propertyBuilder}
}}
}}
", Encoding.UTF8));
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "MIME003",
                    "Generator Error",
                    "Error generating mime types: {0}",
                    "MimeTypes",
                    DiagnosticSeverity.Error,
                    true),
                Location.None,
                ex.ToString()));
        }
    }

    private string GetMimeTypesPath(GeneratorExecutionContext context)
    {
        var additionalFile = context.AdditionalFiles.FirstOrDefault(static file =>
            string.Equals(Path.GetFileName(file.Path), "mimeTypes.json", StringComparison.OrdinalIgnoreCase));
        if (additionalFile != null)
        {
            return additionalFile.Path;
        }

        // Try to find mimeTypes.json in the project directory
        var compilation = context.Compilation;
        var projectDir = Path.GetDirectoryName(compilation.SyntaxTrees.First().FilePath);
        
        var possiblePaths = new[]
        {
            // Try current directory
            Path.Combine(Directory.GetCurrentDirectory(), "mimeTypes.json"),
            // Try project directory
            Path.Combine(projectDir ?? "", "mimeTypes.json"),
            // Try one level up (solution directory)
            Path.Combine(Directory.GetParent(projectDir ?? "")?.FullName ?? "", "mimeTypes.json"),
            // Try in the Generator project
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mimeTypes.json")
        };

        return possiblePaths.FirstOrDefault(File.Exists) ?? possiblePaths[0];
    }

    private string GetMetadataPath(string mimeTypesPath, GeneratorExecutionContext context)
    {
        var additionalFile = context.AdditionalFiles.FirstOrDefault(static file =>
            string.Equals(Path.GetFileName(file.Path), "mimeTypes.metadata.json", StringComparison.OrdinalIgnoreCase));
        if (additionalFile != null)
        {
            return additionalFile.Path;
        }

        var mimeTypesDirectory = Path.GetDirectoryName(mimeTypesPath);
        var compilation = context.Compilation;
        var projectDir = Path.GetDirectoryName(compilation.SyntaxTrees.First().FilePath);

        var possiblePaths = new[]
        {
            Path.Combine(mimeTypesDirectory ?? "", "mimeTypes.metadata.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "mimeTypes.metadata.json"),
            Path.Combine(projectDir ?? "", "mimeTypes.metadata.json"),
            Path.Combine(Directory.GetParent(projectDir ?? "")?.FullName ?? "", "mimeTypes.metadata.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mimeTypes.metadata.json")
        };

        return possiblePaths.FirstOrDefault(File.Exists) ?? possiblePaths[0];
    }

    private static string BuildMimeTypeInfoInitializer(string fallbackMime, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var mime = GetString(element, "mime") ?? fallbackMime;
        if (string.IsNullOrWhiteSpace(mime))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("new MimeTypeInfo");
        builder.AppendLine("{");
        builder.AppendLine($"Mime = {Literal(mime)},");
        builder.AppendLine($"Extensions = {StringArray(GetStringArray(element, "extensions"))},");
        builder.AppendLine($"IsIanaRegistered = {BoolLiteral(GetBool(element, "isIanaRegistered"))},");
        builder.AppendLine($"IsObsolete = {BoolLiteral(GetBool(element, "isObsolete"))},");
        AppendNullableString(builder, "PreferredMime", GetString(element, "preferredMime"));
        AppendNullableString(builder, "Template", GetString(element, "template"));
        AppendNullableString(builder, "TemplateUrl", GetString(element, "templateUrl"));
        AppendNullableString(builder, "Source", GetString(element, "source"));
        AppendNullableString(builder, "Registered", GetString(element, "registered"));
        AppendNullableString(builder, "Updated", GetString(element, "updated"));
        AppendNullableString(builder, "IntendedUsage", GetString(element, "intendedUsage"));
        AppendNullableString(builder, "EncodingConsiderations", GetString(element, "encodingConsiderations"));
        AppendNullableString(builder, "PublishedSpecification", GetString(element, "publishedSpecification"));
        AppendNullableString(builder, "Applications", GetString(element, "applications"));
        builder.AppendLine($"DeprecatedAliases = {StringArray(GetStringArray(element, "deprecatedAliases"))},");
        builder.AppendLine($"References = {StringArray(GetStringArray(element, "references"))},");
        builder.AppendLine($"MagicSignatures = {MagicSignatureArray(element)},");
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendNullableString(StringBuilder builder, string propertyName, string? value)
    {
        if (value == null)
        {
            return;
        }

        builder.AppendLine($"{propertyName} = {Literal(value)},");
    }

    private static string MagicSignatureArray(JsonElement element)
    {
        if (!element.TryGetProperty("magicSignatures", out var signatures) || signatures.ValueKind != JsonValueKind.Array)
        {
            return "Array.Empty<MimeMagicSignature>()";
        }

        var items = new List<string>();
        foreach (var signature in signatures.EnumerateArray())
        {
            if (signature.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var raw = GetString(signature, "raw");
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var bytes = GetByteArray(signature, "bytes");
            var hex = GetString(signature, "hex");
            var offset = GetInt(signature, "offset");
            items.Add($@"new MimeMagicSignature
{{
Raw = {Literal(raw)},
Bytes = {ByteArray(bytes)},
Hex = {Literal(hex)},
Offset = {offset.ToString(System.Globalization.CultureInfo.InvariantCulture)}
}}");
        }

        return items.Count == 0
            ? "Array.Empty<MimeMagicSignature>()"
            : "new[]\n{\n" + string.Join(",\n", items) + "\n}";
    }

    private static string StringArray(IReadOnlyList<string> values)
    {
        return values.Count == 0
            ? "Array.Empty<string>()"
            : "new[] { " + string.Join(", ", values.Select(Literal)) + " }";
    }

    private static string ByteArray(IReadOnlyList<byte> values)
    {
        return values.Count == 0
            ? "ImmutableArray<byte>.Empty"
            : "ImmutableArray.Create<byte>(" + string.Join(", ", values.Select(static value => $"0x{value:X2}")) + ")";
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static IReadOnlyList<byte> GetByteArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<byte>();
        }

        var values = new List<byte>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetByte(out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool GetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
            property.GetBoolean();
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static string BoolLiteral(bool value)
    {
        return value ? "true" : "false";
    }

    private static string ParseKey(string key)
    {
        if (char.IsDigit(key[0]))
        {
            key = "_" + key;
        }

        key = key.Replace("-", "_").Replace('.', '_');

        return key.ToUpperInvariant();
    }

    private static string Literal(string? value)
    {
        return value == null ? "null" : $"\"{Escape(value)}\"";
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }
}
