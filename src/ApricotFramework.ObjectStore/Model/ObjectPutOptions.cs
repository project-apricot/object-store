namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// How an object is written.
/// </summary>
public sealed record ObjectPutOptions
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

    /// <summary>
    /// Gets the condition that makes the write operation create-only: <c>*</c> fails the write operation when the object
    /// already exists.
    /// </summary>
    /// <remarks>
    /// A failed condition throws <see cref="Exceptions.ObjectAlreadyExistsException"/>. Any other value is
    /// treated as an ETag the existing object must not have.
    /// </remarks>
    public string? IfNoneMatch { get; init; }

    /// <summary>
    /// Gets the ETag the existing object must have for the write to go ahead, or null for none.
    /// </summary>
    /// <remarks>
    /// A failed condition throws an <see cref="Exceptions.ObjectStoreException"/> with
    /// <see cref="Exceptions.ObjectStoreErrorCode.PreconditionFailed"/>.
    /// </remarks>
    public string? IfMatch { get; init; }
}
