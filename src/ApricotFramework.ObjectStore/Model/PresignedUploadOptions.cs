namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// How a presigned upload URL is made.
/// </summary>
/// <remarks>
/// Whatever is set here is signed, so the client must send exactly the headers returned in
/// <see cref="PresignedRequest.RequiredHeaders"/> or the store refuses the request. A signed URL cannot
/// limit an upload's size; use a presigned form for that.
/// </remarks>
public sealed record PresignedUploadOptions
{
    /// <summary>
    /// Gets how long the URL works, or null for the store's default.
    /// </summary>
    public TimeSpan? Expiry { get; init; }

    /// <summary>
    /// Gets the content type the upload must carry, or null to leave it open.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the user metadata the upload must carry, or null for none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
