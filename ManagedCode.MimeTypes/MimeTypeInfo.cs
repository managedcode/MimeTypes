using System;
using System.Collections.Generic;

namespace ManagedCode.MimeTypes;

/// <summary>
/// Describes registry metadata known for a MIME type.
/// </summary>
public sealed record MimeTypeInfo
{
    /// <summary>
    /// Gets the MIME type value.
    /// </summary>
    public required string Mime { get; init; }

    /// <summary>
    /// Gets the known dot-prefixed extensions for this MIME type.
    /// </summary>
    public IReadOnlyCollection<string> Extensions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets whether this MIME type is registered in the IANA media types registry.
    /// </summary>
    public bool IsIanaRegistered { get; init; }

    /// <summary>
    /// Gets whether IANA marks this MIME type as obsolete.
    /// </summary>
    public bool IsObsolete { get; init; }

    /// <summary>
    /// Gets the preferred MIME type when this registration is obsolete and IANA identifies a replacement.
    /// </summary>
    public string? PreferredMime { get; init; }

    /// <summary>
    /// Gets the IANA template path when available.
    /// </summary>
    public string? Template { get; init; }

    /// <summary>
    /// Gets the IANA template URL when available.
    /// </summary>
    public string? TemplateUrl { get; init; }

    /// <summary>
    /// Gets the source that supplied this metadata.
    /// </summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>
    /// Gets the registration date when available.
    /// </summary>
    public string? Registered { get; init; }

    /// <summary>
    /// Gets the registry update date when available.
    /// </summary>
    public string? Updated { get; init; }

    /// <summary>
    /// Gets the intended usage field from the registration template.
    /// </summary>
    public string? IntendedUsage { get; init; }

    /// <summary>
    /// Gets the encoding considerations field from the registration template.
    /// </summary>
    public string? EncodingConsiderations { get; init; }

    /// <summary>
    /// Gets the published specification field from the registration template.
    /// </summary>
    public string? PublishedSpecification { get; init; }

    /// <summary>
    /// Gets the applications field from the registration template.
    /// </summary>
    public string? Applications { get; init; }

    /// <summary>
    /// Gets deprecated aliases listed by the registration template.
    /// </summary>
    public IReadOnlyCollection<string> DeprecatedAliases { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets registry references associated with this MIME type.
    /// </summary>
    public IReadOnlyCollection<string> References { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Gets magic-number metadata from the registration template.
    /// </summary>
    public IReadOnlyCollection<MimeMagicSignature> MagicSignatures { get; init; } = Array.Empty<MimeMagicSignature>();
}
