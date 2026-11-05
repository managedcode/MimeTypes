using System;
using System.IO;
using Shouldly;
using Xunit;

namespace ManagedCode.MimeTypes.Tests;

public class SignatureCoverageTests
{
    [Theory]
    [InlineData("255044462D", "pdf", "application/pdf")]
    [InlineData("FFD8FF", "jpg", "image/jpeg")]
    [InlineData("89504E470D0A1A0A", "png", "image/png")]
    [InlineData("474946383761", "gif", "image/gif")]
    [InlineData("474946383961", "gif", "image/gif")]
    [InlineData("424D", "bmp", "image/bmp")]
    [InlineData("49492A00", "tif", "image/tiff")]
    [InlineData("4D4D002A", "tif", "image/tiff")]
    [InlineData("00000100", "ico", "image/x-icon")]
    [InlineData("38425053", "psd", "image/vnd.adobe.photoshop")]
    [InlineData("1F8B", "gz", "application/gzip")]
    [InlineData("425A68", "bz2", "application/x-bzip2")]
    [InlineData("526172211A0700", "rar", "application/x-rar-compressed")]
    [InlineData("526172211A070100", "rar", "application/x-rar-compressed")]
    [InlineData("377ABCAF271C", "7z", "application/x-7z-compressed")]
    [InlineData("FD377A585A00", "xz", "application/x-xz")]
    [InlineData("664C6143", "flac", "audio/x-flac")]
    [InlineData("494433", "mp3", "audio/mpeg")]
    [InlineData("53514C69746520666F726D6174203300", "sqlite", "application/vnd.sqlite3")]
    [InlineData("25215053", "ps", "application/postscript")]
    [InlineData("7F454C46", null, "application/x-executable")]
    [InlineData("4D5A9000", "exe", "application/x-msdownload")]
    [InlineData("7B5C7274", "rtf", "application/rtf")]
    [InlineData("64383A616E6E6F756E6365", "torrent", "application/x-bittorrent")]
    public void IndependentSignature_ShouldAgreeWithCatalogAndValidation(string hex, string? extension, string expected)
    {
        var bytes = Convert.FromHexString(hex);
        using var stream = new MemoryStream(bytes);
        MimeHelper.GetMimeTypeByContent(stream).ShouldBe(expected);
        stream.Position.ShouldBe(0);
        if (extension != null)
        {
            MimeHelper.GetMimeType("file." + extension).ShouldBe(expected);
            MimeHelper.MatchesExtensionByContent("file." + extension, stream).ShouldBeTrue();
        }

        using var nonSeekable = new OfficeContentDetectionTests.NonSeekableStream(new MemoryStream(bytes));
        MimeHelper.GetMimeTypeByContent(nonSeekable).ShouldBe(expected);

        bytes[0] ^= 0xFF;
        using var nearMatch = new MemoryStream(bytes);
        MimeHelper.GetMimeTypeByContent(nearMatch).ShouldNotBe(expected);
    }
}
