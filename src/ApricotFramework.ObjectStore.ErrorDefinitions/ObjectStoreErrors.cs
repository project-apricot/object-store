namespace ApricotFramework.ObjectStore.ErrorDefinitions;

/// <summary>
/// Every error code this library sends to a client.
/// </summary>
/// <remarks>
/// A client needs message text for exactly these plus the codes its own service sends. Failures that are
/// the service's own fault — its credentials, its configuration, a bug — share one code, so the set a
/// client must handle does not grow every time a new way to fail is recognized.
/// </remarks>
public static class ObjectStoreErrors
{
    /// <summary>
    /// The object does not exist. Reported as not found.
    /// </summary>
    public const string ObjectNotFound = "OBJECT_NOT_FOUND";

    /// <summary>
    /// A create-only write found the object already there. Reported as already exists.
    /// </summary>
    public const string ObjectAlreadyExists = "OBJECT_ALREADY_EXISTS";

    /// <summary>
    /// A condition the caller put on the request, such as a matching ETag, did not hold. Reported as a
    /// failed precondition.
    /// </summary>
    public const string ObjectPreconditionFailed = "OBJECT_PRECONDITION_FAILED";

    /// <summary>
    /// The object key is not usable. Reported as a validation failure.
    /// </summary>
    public const string ObjectKeyInvalid = "OBJECT_KEY_INVALID";

    /// <summary>
    /// The request is not acceptable, such as a part number out of range. Reported as a validation
    /// failure.
    /// </summary>
    public const string ObjectRequestInvalid = "OBJECT_REQUEST_INVALID";

    /// <summary>
    /// The store could not answer now: it is rate limiting, unreachable, or too slow. Reported as the
    /// service being unavailable.
    /// </summary>
    public const string StoreUnavailable = "OBJECT_STORE_UNAVAILABLE";

    /// <summary>
    /// The store failed for a reason that is not the caller's: refused credentials, a missing bucket, an
    /// operation the provider lacks, or something unclassified. Reported as an internal error.
    /// </summary>
    public const string StoreFailed = "OBJECT_STORE_FAILED";
}
