using ManagedCode.MimeTypes;

var mime = MimeHelper.GetMimeType("asset.png");
var extensions = MimeHelper.GetExtensions(mime);
var knownMimeTypes = MimeHelper.GetKnownMimeTypes();
var before = GC.GetAllocatedBytesForCurrentThread();
for (var index = 0; index < 100_000; index++)
{
    _ = MimeHelper.GetMimeType("https://cdn.example.test/assets/photo.png?v=1");
}
var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

Console.WriteLine($"{mime}|{extensions.Count}|{knownMimeTypes.Count}|{allocated}");
