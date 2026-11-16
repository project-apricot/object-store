using ApricotFramework.ErrorDefinitions;
using ApricotFramework.ErrorDefinitions.AspNetCore;
using ApricotFramework.ObjectStore.Exceptions;
using Microsoft.AspNetCore.Http;

namespace ApricotFramework.ObjectStore.ErrorDefinitions;

/// <summary>
/// Reports object store failures as classified errors.
/// </summary>
/// <remarks>
/// What the caller did wrong is reported as such, and what the service did wrong is not blamed on the
/// caller: a refused credential or a missing bucket is ours, so it is an internal error rather than a
/// 403 the client cannot act on. No exception message, store name, bucket, endpoint or key reaches the
/// response — the first three are internal labels, and a key often echoes whatever the caller sent.
/// </remarks>
internal sealed class ObjectStoreExceptionMapper : IExceptionErrorMapper
{
    /// <inheritdoc />
    public IReadOnlyList<ErrorDefinition>? Map(HttpContext httpContext, Exception exception)
    {
        // Null for anything else, so every mapper registered after this one still gets its turn.
        if (exception is not ObjectStoreException store)
        {
            return null;
        }

        return [Describe(store)];
    }

    private static ErrorDefinition Describe(ObjectStoreException exception)
    {
        return exception.ErrorCode switch
        {
            ObjectStoreErrorCode.NotFound => Err.NotFound(ObjectStoreErrors.ObjectNotFound, "The object was not found."),
            ObjectStoreErrorCode.AlreadyExists => Err.AlreadyExists(ObjectStoreErrors.ObjectAlreadyExists, "The object already exists."),
            ObjectStoreErrorCode.PreconditionFailed => Err.PreconditionFailed(ObjectStoreErrors.ObjectPreconditionFailed, "A condition on the request did not hold."),
            ObjectStoreErrorCode.InvalidKey => Err.Validation(ObjectStoreErrors.ObjectKeyInvalid, "The object key is not valid."),
            ObjectStoreErrorCode.InvalidRequest => Err.Validation(ObjectStoreErrors.ObjectRequestInvalid, "The request is not valid."),

            // Retryable is spelled out so a client does not have to know which codes are transient.
            ObjectStoreErrorCode.Throttled or ObjectStoreErrorCode.Unavailable or ObjectStoreErrorCode.Timeout => Err.Unavailable(
                ObjectStoreErrors.StoreUnavailable,
                "The object store is temporarily unable to answer.",
                new Dictionary<string, object?> { ["retryable"] = true }),

            _ => Err.Internal(ObjectStoreErrors.StoreFailed, "The object store failed."),
        };
    }
}
