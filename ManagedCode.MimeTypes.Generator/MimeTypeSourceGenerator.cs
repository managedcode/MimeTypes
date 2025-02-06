using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ManagedCode.MimeTypes.Generator;

[Generator]
public class MimeTypeSourceGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }
#endif
    }

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

            var mime = JObject.Parse(File.ReadAllText(mimeTypesPath));
            var properties = mime.Properties().ToList();
            
            context.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "MIME002",
                    "MimeTypes loaded",
                    "Successfully loaded {0} mime types",
                    "MimeTypes",
                    DiagnosticSeverity.Info,
                    true),
                Location.None,
                properties.Count));

            StringBuilder defineDictionaryBuilder = new();
            StringBuilder propertyBuilder = new();
            Dictionary<string, string> types = new Dictionary<string, string>();

            foreach (var item in properties)
            {
                defineDictionaryBuilder.AppendLine($"MimeTypes.Add(string.Intern(\"{item.Name}\"),string.Intern(\"{item.Value}\"));");
                types[ParseKey(item.Name)] = item.Value.ToString();
            }

            foreach (var item in types)
            {
                propertyBuilder.AppendLine($"public static string {item.Key} => \"{item.Value}\";");
            }
            
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

    private string ParseKey(string key)
    {
        if (char.IsDigit(key[0]))
        {
            key = "_" + key;
        }
        
        key = key.Replace("-", "_");

        return key.ToUpperInvariant();
    }
}


