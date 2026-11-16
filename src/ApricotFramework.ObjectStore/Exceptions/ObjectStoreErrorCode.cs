namespace ApricotFramework.ObjectStore.Exceptions;

/// <summary>
/// Why an object store operation failed.
/// </summary>
/// <remarks>
/// The code is the part a caller should branch on; the exception message carries the detail for a log.
/// Codes are only ever added, so a switch that handles the ones it knows and treats the rest as a
/// failure keeps working.
/// </remarks>
public enum ObjectStoreErrorCode
{
    /// <summary>
    /// The failure was not classified.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The object, or the multipart upload, does not exist.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// A create-only write found the object already there.
    /// </summary>
    AlreadyExists = 2,

    /// <summary>
    /// A conditional request's condition, such as a matching ETag, did not hold.
    /// </summary>
    PreconditionFailed = 3,

    /// <summary>
    /// The object key is not usable, such as an empty key or one containing a path traversal.
    /// </summary>
    InvalidKey = 4,

    /// <summary>
    /// The request itself is not acceptable, such as a part number out of range.
    /// </summary>
    InvalidRequest = 5,

    /// <summary>
    /// The store refused the credentials it was configured with, or they lack the permission.
    /// </summary>
    AccessDenied = 6,

    /// <summary>
    /// The store refused the request for now because a rate or quota limit was reached.
    /// </summary>
    Throttled = 7,

    /// <summary>
    /// The store could not be reached, or answered with a server error.
    /// </summary>
    Unavailable = 8,

    /// <summary>
    /// The store did not answer in time.
    /// </summary>
    Timeout = 9,

    /// <summary>
    /// The store's configuration is unusable, such as a missing bucket setting or a bucket that does not
    /// exist.
    /// </summary>
    Configuration = 10,

    /// <summary>
    /// No store is configured under the requested name.
    /// </summary>
    StoreNotFound = 11,

    /// <summary>
    /// The store's provider does not support the operation.
    /// </summary>
    NotSupported = 12,
}
