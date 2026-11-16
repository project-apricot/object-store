namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// How a multipart upload is started.
/// </summary>
public sealed record MultipartUploadOptions
{
    /// <summary>
    /// Gets the content type to store, or null to leave it to the store.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the <c>Content-Disposition</c> to store, or null for none.
    /// </summary>
    public string? ContentDisposition { get; init; }

    /// <summary>
    /// Gets the <c>Cache-Control</c> to store, or null for none.
    /// </summary>
    public string? CacheControl { get; init; }

    /// <summary>
    /// Gets the user metadata to store, or null for none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
