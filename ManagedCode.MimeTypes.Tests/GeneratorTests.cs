using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using ManagedCode.MimeTypes.Generator;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class GeneratorTests
{
    [Fact]
    public void ExtensionsTest()
    {
        MimeHelper.GetMimeType("pdf").Should().Be("application/pdf");
        MimeHelper.GetMimeType(".gz").Should().Be("application/gzip");
        MimeHelper.GetMimeType("word.docx").Should().Be("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        MimeHelper.GetMimeType("C:\\\\users\\file.txt").Should().Be("text/plain");
    }
    
    [Fact]
    public void EmptyExtensionsTest()
    {
        MimeHelper.GetMimeType("").Should().Be("application/octet-stream");
        MimeHelper.GetMimeType("     ").Should().Be("application/octet-stream");
        MimeHelper.GetMimeType(null).Should().Be("application/octet-stream");
    }
    
    [Fact]
    public void PropertyTest()
    {
        MimeHelper.PDF.Should().Be("application/pdf");
        MimeHelper.GZ.Should().Be("application/gzip");
    }
}