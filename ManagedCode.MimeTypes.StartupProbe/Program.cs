using System.Runtime.CompilerServices;
using ManagedCode.MimeTypes;

var input = args.Length == 0 ? "https://cdn.example.test/assets/photo.png?v=1" : args[0];
var allocationControl = args.Length > 1 && args[1] == "--allocation-control";
var mime = MimeHelper.GetMimeType("asset.png");
var extensions = MimeHelper.GetExtensions(mime);
var knownMimeTypes = MimeHelper.GetKnownMimeTypes();
var result = MeasureLookups(input, allocationControl);

Report(result.Mime, extensions.Count, knownMimeTypes.Count, result.Allocated);

// Keep output infrastructure outside the lookup allocation measurement.
[MethodImpl(MethodImplOptions.NoInlining)]
static void Report(string mime, int extensionCount, int knownCount, long allocated)
{
    Console.WriteLine($"{mime}|{extensionCount}|{knownCount}|{allocated}");
}

// Only the harness bypasses tiering: the library keeps the requested JIT mode.
// This prevents tier transitions of the measuring loop from polluting its counter.
[MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
static (string Mime, long Allocated) MeasureLookups(string input, bool allocationControl)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    if (allocationControl)
    {
        GC.KeepAlive(new byte[1024]);
    }

    var mime = string.Empty;
    for (var index = 0; index < 100_000; index++)
    {
        mime = MimeHelper.GetMimeType(input);
    }

    return (mime, GC.GetAllocatedBytesForCurrentThread() - before);
}
