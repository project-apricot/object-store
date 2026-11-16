namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// How a presigned upload form is made.
/// </summary>
public sealed record PresignedPostOptions
{
    /// <summary>
    /// Gets how long the form works, or null for the store's default.
    /// </summary>
    public TimeSpan? Expiry { get; init; }

    /// <summary>
    /// Gets the content type the upload must carry, or null to leave it open.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets the smallest upload accepted, in bytes, or null for no minimum.
    /// </summary>
    public long? MinimumSize { get; init; }

    /// <summary>
    /// Gets the largest upload accepted, in bytes, or null for no maximum.
    /// </summary>
    /// <remarks>
    /// The reason to use a form at all: it is the only presigned upload the store itself holds to a size.
    /// </remarks>
    public long? MaximumSize { get; init; }

    /// <summary>
    /// Gets the user metadata the upload must carry, or null for none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
