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
    public void Initialize(GeneratorInitializationContext context) {}

    public void Execute(GeneratorExecutionContext context)
    {
        
#if DEBUG
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }
#endif
        var mime = JObject.Parse(File.ReadAllText("mimeTypes.json")).Properties().ToList();
       
         StringBuilder defineDictionaryBuilder = new();
         StringBuilder propertyBuilder = new();
         Dictionary<string, string> types = new Dictionary<string, string>();

         foreach (var item in mime)
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
{defineDictionaryBuilder.ToString()}
}}
{propertyBuilder.ToString()}
}}
}}
", Encoding.UTF8));
    }

    private string ParseKey(string key)
    {
        if (char.IsDigit(key[0]))
        {
            key = $"_" + key;
        }
        
        key = key.Replace("-", "_");

        return key.ToUpperInvariant();
    }
}


