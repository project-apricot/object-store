namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// How an object is copied.
/// </summary>
/// <remarks>
/// Left empty, the copy keeps everything the source has: content type, <c>Content-Disposition</c>,
/// <c>Cache-Control</c> and metadata. Setting any one of them replaces all four: the store cannot merge them,
/// so whatever is not named is dropped.
/// </remarks>
public sealed record ObjectCopyOptions
{
    /// <summary>
    /// Gets the content type for the copy, or null to keep the source's.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the <c>Content-Disposition</c> for the copy, or null to keep the source's.
    /// </summary>
    public string? ContentDisposition { get; init; }

    /// <summary>
    /// Gets the <c>Cache-Control</c> for the copy, or null to keep the source's.
    /// </summary>
    public string? CacheControl { get; init; }

    /// <summary>
    /// Gets the user metadata for the copy, or null to keep the source's.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    /// Gets whether the copy replaces what the source has rather than keeping it.
    /// </summary>
    public bool ReplacesMetadata => this.ContentType is not null
        || this.ContentDisposition is not null
        || this.CacheControl is not null
        || this.Metadata is not null;
}
