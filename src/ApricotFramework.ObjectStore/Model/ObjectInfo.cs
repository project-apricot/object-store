using System.Collections.Frozen;

namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// What a store knows about one object, without its content.
/// </summary>
/// <remarks>
/// A listing reports only what the store keeps in its index: key, size, ETag and modification time.
/// <see cref="ContentType"/>, <see cref="ContentDisposition"/>, <see cref="CacheControl"/> and
/// <see cref="Metadata"/> are filled in by operations that read the object itself, such as a head or a get.
/// </remarks>
public sealed record ObjectInfo
{
    /// <summary>
    /// Gets the object key, relative to the store's prefix.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Gets the size of the whole object in bytes, even when a range of it was read.
    /// </summary>
    public required long Size { get; init; }

    /// <summary>
    /// Gets the ETag the store assigned, or null when it reported none.
    /// </summary>
    /// <remarks>
    /// Opaque: compare it, do not interpret it. It is the content hash only for a simple upload, and
    /// only on some servers.
    /// </remarks>
    public string? ETag { get; init; }

    /// <summary>
    /// Gets when the object was last modified, or null when the store did not say.
    /// </summary>
    public DateTimeOffset? LastModified { get; init; }

    /// <summary>
    /// Gets the content type, or null when unknown.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the <c>Content-Disposition</c> stored with the object, or null when there is none or it was not read.
    /// </summary>
    public string? ContentDisposition { get; init; }

    /// <summary>
    /// Gets the <c>Cache-Control</c> stored with the object, or null when there is none or it was not read.
    /// </summary>
    public string? CacheControl { get; init; }

    /// <summary>
    /// Gets the user metadata, matched case-insensitively. Empty when there is none, or it was not read.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = EmptyMetadata;

    /// <summary>
    /// Gets a metadata dictionary with nothing in it, which is safe to share because it cannot be changed.
    /// </summary>
    public static IReadOnlyDictionary<string, string> EmptyMetadata { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
}
