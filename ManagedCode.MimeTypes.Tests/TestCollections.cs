using Xunit;

namespace ManagedCode.MimeTypes.Tests;

/// <summary>
/// Provides the shared collection name used by tests that mutate global MIME helper state.
/// </summary>
public static class MimeHelperMutableStateCollection
{
    public const string Name = "MimeHelper.MutableState";
}

/// <summary>
/// Ensures tests touching <see cref="MimeHelper"/> state run in isolation.
/// </summary>
[CollectionDefinition(MimeHelperMutableStateCollection.Name, DisableParallelization = true)]
public sealed class MimeHelperMutableStateCollectionDefinition
{
}
