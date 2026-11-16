namespace ApricotFramework.ObjectStore.Exceptions;

/// <summary>
/// Thrown when a create-only write finds the object already there.
/// </summary>
public class ObjectAlreadyExistsException : ObjectStoreException
{
    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    public ObjectAlreadyExistsException()
        : base(ObjectStoreErrorCode.AlreadyExists, "The object already exists.")
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What already exists.</param>
    public ObjectAlreadyExistsException(string message)
        : base(ObjectStoreErrorCode.AlreadyExists, message)
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What already exists.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectAlreadyExistsException(string message, Exception innerException)
        : base(ObjectStoreErrorCode.AlreadyExists, message, innerException: innerException)
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What already exists.</param>
    /// <param name="storeName">The store that was written to.</param>
    /// <param name="provider">The provider of that store.</param>
    /// <param name="key">The key that already exists.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectAlreadyExistsException(string message, string? storeName, string? provider, string? key, Exception? innerException = null)
        : base(ObjectStoreErrorCode.AlreadyExists, message, storeName, provider, key, innerException)
    {
    }
}
