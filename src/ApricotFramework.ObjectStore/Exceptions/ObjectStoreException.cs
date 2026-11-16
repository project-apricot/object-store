namespace ApricotFramework.ObjectStore.Exceptions;

/// <summary>
/// Thrown when an object store operation fails.
/// </summary>
/// <remarks>
/// Wraps whatever the underlying SDK threw, which stays available as the inner exception for a log. The
/// message names the store, never a setting value, so a credential cannot reach a log through one.
/// Cancellation the caller asked for is not wrapped: it still surfaces as
/// <see cref="OperationCanceledException"/>.
/// </remarks>
public class ObjectStoreException : Exception
{
    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    public ObjectStoreException()
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public ObjectStoreException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a new instance of the exception.
    /// </summary>
    /// <param name="errorCode">Why the operation failed.</param>
    /// <param name="message">What went wrong.</param>
    /// <param name="storeName">The store the operation was against, when known.</param>
    /// <param name="provider">The provider of that store, when known.</param>
    /// <param name="key">The object key involved, when there was one.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ObjectStoreException(
        ObjectStoreErrorCode errorCode,
        string message,
        string? storeName = null,
        string? provider = null,
        string? key = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        this.ErrorCode = errorCode;
        this.StoreName = storeName;
        this.Provider = provider;
        this.Key = key;
    }

    /// <summary>
    /// Gets why the operation failed.
    /// </summary>
    public ObjectStoreErrorCode ErrorCode { get; }

    /// <summary>
    /// Gets the name of the store the operation was against, when known.
    /// </summary>
    /// <remarks>
    /// An internal label the host chose, so it belongs in a log rather than in anything a client sees.
    /// </remarks>
    public string? StoreName { get; }

    /// <summary>
    /// Gets the provider of that store, such as <c>s3</c>, when known.
    /// </summary>
    public string? Provider { get; }

    /// <summary>
    /// Gets the object key involved, or null when the operation had none.
    /// </summary>
    /// <remarks>
    /// Keys can carry user data, so treat this as log-only.
    /// </remarks>
    public string? Key { get; }

    /// <summary>
    /// Decides whether the same operation is worth trying again.
    /// </summary>
    /// <returns>
    /// True when the failure was a condition that typically clears: a rate limit, an unreachable store,
    /// or a timeout. False for every failure that will recur.
    /// </returns>
    public bool IsTransient()
    {
        return this.ErrorCode is ObjectStoreErrorCode.Throttled or ObjectStoreErrorCode.Unavailable or ObjectStoreErrorCode.Timeout;
    }
}
