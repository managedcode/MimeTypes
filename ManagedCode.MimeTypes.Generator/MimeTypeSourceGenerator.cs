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

            foreach (var item in types)
            {
                propertyBuilder.AppendLine($"public static string {item.Key} => \"{Escape(item.Value)}\";");
            }

            context.ReportDiagnostic(Diagnostic.Create(MimeTypesLoadedDiagnostic, Location.None, types.Count));
            
            context.AddSource("MimeHelper.Properties.cs", SourceText.From(@$"
namespace ManagedCode.MimeTypes
{{
public static partial class MimeHelper
{{
static partial void Init()
{{
{defineDictionaryBuilder}
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

    private static string ParseKey(string key)
    {
        if (char.IsDigit(key[0]))
        {
            key = "_" + key;
        }

        key = key.Replace("-", "_").Replace('.', '_');

        return key.ToUpperInvariant();
    }

    private static string Escape(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }
}
