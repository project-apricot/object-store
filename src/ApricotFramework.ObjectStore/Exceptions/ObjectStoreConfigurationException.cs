namespace ApricotFramework.ObjectStore.Exceptions;

/// <summary>
/// Thrown when a store's settings cannot be read as its provider needs them.
/// </summary>
/// <remarks>
/// Messages name the store and the setting but never the value, so a credential cannot reach a log
/// through one.
/// </remarks>
public class ObjectStoreConfigurationException : ObjectStoreException
{
    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    public ObjectStoreConfigurationException()
        : base(ObjectStoreErrorCode.Configuration, "The object store is misconfigured.")
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What could not be read.</param>
    public ObjectStoreConfigurationException(string message)
        : base(ObjectStoreErrorCode.Configuration, message)
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What could not be read.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectStoreConfigurationException(string message, Exception innerException)
        : base(ObjectStoreErrorCode.Configuration, message, innerException: innerException)
    {
    }
}
