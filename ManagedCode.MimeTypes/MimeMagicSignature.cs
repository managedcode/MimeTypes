using System.Collections.Immutable;

namespace ManagedCode.MimeTypes;

/// <summary>
/// Describes magic-number metadata published for a MIME type registration.
/// </summary>
public sealed record MimeMagicSignature
{
    /// <summary>
    /// Gets the raw magic-number text from the registry template.
    /// </summary>
    public required string Raw { get; init; }

    /// <summary>
    /// Gets the parsed byte prefix when the registry text is machine-readable.
    /// </summary>
    public ImmutableArray<byte> Bytes { get; init; } = ImmutableArray<byte>.Empty;

    /// <summary>
    /// Gets the parsed byte prefix as hexadecimal text when available.
    /// </summary>
    public string? Hex { get; init; }

    /// <summary>
    /// Gets the byte offset where the signature applies.
    /// </summary>
    public int Offset { get; init; }

    /// <summary>
    /// Gets whether this metadata contains a parsed byte prefix.
    /// </summary>
    public bool IsBytePrefix => !Bytes.IsDefaultOrEmpty;
}
