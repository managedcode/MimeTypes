using System;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ManagedCode.MimeTypes.Generator;

/// <summary>
/// Emits source that bootstraps MIME mappings and exposes typed constants for each extension.
/// </summary>
[Generator]
public sealed class MimeTypeSourceGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MimeTypesMissingDiagnostic = new(
        "MIME001",
        "MIME catalog input is missing",
        "Exactly one mimeTypes.json AdditionalFile is required",
        "MimeTypes",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MimeTypesLoadedDiagnostic = new(
        "MIME002",
        "MimeTypes loaded",
        "Successfully loaded {0} mime types",
        "MimeTypes",
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor GeneratorErrorDiagnostic = new(
        "MIME003",
        "MIME source generation failed",
        "Error generating MIME types: {0}",
        "MimeTypes",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var mimeTypes = context.AdditionalTextsProvider
            .Where(static file => string.Equals(Path.GetFileName(file.Path), "mimeTypes.json", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) => file.GetText(cancellationToken)?.ToString())
            .Collect();

        var metadata = context.AdditionalTextsProvider
            .Where(static file => string.Equals(Path.GetFileName(file.Path), "mimeTypes.metadata.json", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellationToken) => file.GetText(cancellationToken)?.ToString())
            .Collect();

        context.RegisterSourceOutput(
            mimeTypes.Combine(metadata),
            static (productionContext, inputs) => Execute(productionContext, inputs.Left, inputs.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        ImmutableArray<string?> mimeTypeSources,
        ImmutableArray<string?> metadataSources)
    {
        try
        {
            if (mimeTypeSources.Length != 1 || string.IsNullOrWhiteSpace(mimeTypeSources[0]))
            {
                context.ReportDiagnostic(Diagnostic.Create(MimeTypesMissingDiagnostic, Location.None));
                return;
            }

            using var document = JsonDocument.Parse(mimeTypeSources[0]!);

            StringBuilder mappingBuilder = new();
            StringBuilder metadataBuilder = new();
            StringBuilder constantBuilder = new();
            Dictionary<string, string> mappings = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> constants = new(StringComparer.Ordinal);

            foreach (var item in document.RootElement.EnumerateObject())
            {
                var extension = item.Name.Trim();
                var mimeValue = item.Value.GetString()?.Trim() ?? string.Empty;

                if (extension.Length == 0 || mimeValue.Length == 0)
                {
                    continue;
                }

                mappings[NormalizeExtension(extension)] = mimeValue;
                constants[ParseKey(extension)] = mimeValue;
            }

            if (metadataSources.Length > 1)
            {
                throw new InvalidOperationException("Only one mimeTypes.metadata.json AdditionalFile is allowed.");
            }

            if (metadataSources.Length == 1 && !string.IsNullOrWhiteSpace(metadataSources[0]))
            {
                using var metadataDocument = JsonDocument.Parse(metadataSources[0]!);
                foreach (var item in metadataDocument.RootElement.EnumerateObject())
                {
                    var initializer = BuildMimeTypeInfoInitializer(item.Name, item.Value);
                    if (initializer.Length > 0)
                    {
                        metadataBuilder.AppendLine($"{initializer},");
                    }
                }
            }

            foreach (var item in mappings)
            {
                mappingBuilder.AppendLine($"new(\"{Escape(item.Key)}\", \"{Escape(item.Value)}\"),");
            }

            foreach (var item in constants)
            {
                constantBuilder.AppendLine($"public const string {item.Key} = \"{Escape(item.Value)}\";");
            }

            context.ReportDiagnostic(Diagnostic.Create(MimeTypesLoadedDiagnostic, Location.None, mappings.Count));

            context.AddSource("MimeHelper.Properties.cs", SourceText.From(@$"
using System;
using System.Collections.Immutable;

namespace ManagedCode.MimeTypes
{{
public static partial class MimeHelper
{{
static partial void Init()
{{
InitializeBuiltInRegistry(
[
{mappingBuilder}
],
[
{metadataBuilder}
]);
}}
{constantBuilder}
}}
}}
", Encoding.UTF8));
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorErrorDiagnostic, Location.None, ex.ToString()));
        }
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
                    values.Add(value!);
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

    private static string NormalizeExtension(string extension)
    {
        return extension.Trim().TrimStart('.').ToLowerInvariant();
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
