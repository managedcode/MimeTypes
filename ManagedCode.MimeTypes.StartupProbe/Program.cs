using System.Runtime.CompilerServices;
using ManagedCode.MimeTypes;

var input = args.Length == 0 ? "https://cdn.example.test/assets/photo.png?v=1" : args[0];
var mime = MimeHelper.GetMimeType("asset.png");
var extensions = MimeHelper.GetExtensions(mime);
var knownMimeTypes = MimeHelper.GetKnownMimeTypes();
var before = GC.GetAllocatedBytesForCurrentThread();
for (var index = 0; index < 100_000; index++)
{
    mime = MimeHelper.GetMimeType(input);
}
var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

Report(mime, extensions.Count, knownMimeTypes.Count, allocated);

// Keep interpolated output in a separate JIT body. Otherwise OSR can initialize
// formatting infrastructure while the lookup allocation counter is running.
[MethodImpl(MethodImplOptions.NoInlining)]
static void Report(string mime, int extensionCount, int knownCount, long allocated)
{
    Console.WriteLine($"{mime}|{extensionCount}|{knownCount}|{allocated}");
}
