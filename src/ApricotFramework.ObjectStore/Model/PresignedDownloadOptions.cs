namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// How a presigned download URL is made.
/// </summary>
public sealed record PresignedDownloadOptions
{
    /// <summary>
    /// Gets how long the URL works, or null for the store's default.
    /// </summary>
    /// <remarks>
    /// A provider that caps the lifetime throws <see cref="Exceptions.ObjectStoreErrorCode.InvalidRequest"/>
    /// beyond it rather than quietly shortening it.
    /// </remarks>
    public TimeSpan? Expiry { get; init; }

    /// <summary>
    /// Gets the <c>Content-Disposition</c> the download is served with, such as
    /// <c>attachment; filename="report.pdf"</c>, or null to serve what is stored.
    /// </summary>
    public string? ContentDisposition { get; init; }

    /// <summary>
    /// Gets the content type the download is served with, or null to serve what is stored.
    /// </summary>
    public string? ContentType { get; init; }
}
