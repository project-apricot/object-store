namespace ApricotFramework.ObjectStore.Exceptions;

/// <summary>
/// Thrown when an object, or a multipart upload, does not exist.
/// </summary>
public class ObjectNotFoundException : ObjectStoreException
{
    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    public ObjectNotFoundException()
        : base(ObjectStoreErrorCode.NotFound, "The object was not found.")
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What was not found.</param>
    public ObjectNotFoundException(string message)
        : base(ObjectStoreErrorCode.NotFound, message)
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What was not found.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectNotFoundException(string message, Exception innerException)
        : base(ObjectStoreErrorCode.NotFound, message, innerException: innerException)
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What was not found.</param>
    /// <param name="storeName">The store that was asked.</param>
    /// <param name="provider">The provider of that store.</param>
    /// <param name="key">The key that was not found.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectNotFoundException(string message, string? storeName, string? provider, string? key, Exception? innerException = null)
        : base(ObjectStoreErrorCode.NotFound, message, storeName, provider, key, innerException)
    {
    }
}
