using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.S3.Impl;

/// <summary>
/// The one place AWS SDK failures become this library's exceptions.
/// </summary>
/// <remarks>
/// The messages carry the vendor's error code and HTTP status and nothing else: the SDK's own message
/// routinely names the bucket, the endpoint and the key. The full exception stays as the inner exception
/// for a log.
/// </remarks>
public static class S3ExceptionTranslator
{
    /// <summary>
    /// Decides whether an exception is a store failure worth translating.
    /// </summary>
    /// <param name="exception">What was thrown.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns>
    /// False for the caller's own cancellation, for an exception that is already this library's, and for
    /// argument mistakes, all of which should surface as they are.
    /// </returns>
    public static bool IsTranslatable(Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is not (ObjectStoreException or ArgumentException or ObjectDisposedException)
            && !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);
    }

    /// <summary>
    /// Translates an SDK failure.
    /// </summary>
    /// <param name="exception">What was thrown.</param>
    /// <param name="storeName">The store the operation was against.</param>
    /// <param name="key">The key involved, or null.</param>
    /// <param name="createOnly">
    /// True when the write was conditional on the object not existing, which turns a failed condition
    /// into <see cref="ObjectStoreErrorCode.AlreadyExists"/>.
    /// </param>
    /// <returns>The exception to throw.</returns>
    public static ObjectStoreException Translate(Exception exception, string storeName, string? key, bool createOnly = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var (code, detail) = Classify(exception, createOnly);

        var message = $"Object store '{storeName}' could not complete the request: {detail}.";

        return code switch
        {
            ObjectStoreErrorCode.NotFound => new ObjectNotFoundException(message, storeName, S3ObjectStoreProvider.ProviderName, key, exception),
            ObjectStoreErrorCode.AlreadyExists => new ObjectAlreadyExistsException(message, storeName, S3ObjectStoreProvider.ProviderName, key, exception),
            _ => new ObjectStoreException(code, message, storeName, S3ObjectStoreProvider.ProviderName, key, exception),
        };
    }

    /// <summary>
    /// Classifies a per-key failure reported inside a batch response.
    /// </summary>
    /// <param name="vendorCode">The error code the server gave.</param>
    /// <returns>The classification.</returns>
    public static ObjectStoreErrorCode ClassifyCode(string? vendorCode)
    {
        return vendorCode switch
        {
            "NoSuchKey" or "NoSuchUpload" or "NotFound" => ObjectStoreErrorCode.NotFound,
            "NoSuchBucket" => ObjectStoreErrorCode.Configuration,
            "PreconditionFailed" => ObjectStoreErrorCode.PreconditionFailed,
            "AccessDenied" or "AllAccessDisabled" or "InvalidAccessKeyId" or "SignatureDoesNotMatch" or "ExpiredToken" or "InvalidToken" or "AccountProblem"
                => ObjectStoreErrorCode.AccessDenied,
            "SlowDown" or "RequestLimitExceeded" or "Throttling" or "ThrottlingException" or "TooManyRequests" or "ServiceUnavailable"
                => ObjectStoreErrorCode.Throttled,
            "RequestTimeout" or "RequestTimeTooSkewed" => ObjectStoreErrorCode.Timeout,
            "InternalError" => ObjectStoreErrorCode.Unavailable,
            "InvalidArgument" or "InvalidRequest" or "InvalidRange" or "EntityTooLarge" or "EntityTooSmall" or "InvalidPart" or "InvalidPartOrder"
                or "MalformedXML" or "InvalidDigest" or "BadDigest" or "KeyTooLongError" or "MetadataTooLarge"
                => ObjectStoreErrorCode.InvalidRequest,
            _ => ObjectStoreErrorCode.Unknown,
        };
    }

    private static (ObjectStoreErrorCode Code, string Detail) Classify(Exception exception, bool createOnly)
    {
        return exception switch
        {
            AmazonServiceException service => ClassifyService(service, createOnly),

            // a timeout the SDK or HttpClient raised, as opposed to the caller giving up
            OperationCanceledException or AmazonClientException { InnerException: OperationCanceledException }
                => (ObjectStoreErrorCode.Timeout, "the request timed out"),

            // a bare IOException is deliberately not here: the HTTP stack wraps network failures in
            // HttpRequestException, so one that arrives unwrapped is far more likely to be the caller's
            // own stream failing, and calling that unavailable would invite a retry that cannot work
            HttpRequestException or SocketException or AmazonClientException { InnerException: HttpRequestException or SocketException or TimeoutException }
                => (ObjectStoreErrorCode.Unavailable, "the store could not be reached"),

            _ => (ObjectStoreErrorCode.Unknown, $"an unexpected {exception.GetType().Name}"),
        };
    }

    private static (ObjectStoreErrorCode Code, string Detail) ClassifyService(AmazonServiceException exception, bool createOnly)
    {
        var status = (int)exception.StatusCode;
        var vendorCode = exception.ErrorCode;
        var detail = $"{vendorCode ?? "error"} (HTTP {status})";

        // a conditional write that lost: either the object was there, or another writer got in first
        if (status == 412 || vendorCode is "PreconditionFailed" or "ConditionalRequestConflict")
        {
            return (createOnly ? ObjectStoreErrorCode.AlreadyExists : ObjectStoreErrorCode.PreconditionFailed, detail);
        }

        var byCode = ClassifyCode(vendorCode);

        if (byCode != ObjectStoreErrorCode.Unknown)
        {
            return (byCode, detail);
        }

        var byStatus = exception.StatusCode switch
        {
            HttpStatusCode.NotFound => ObjectStoreErrorCode.NotFound,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ObjectStoreErrorCode.AccessDenied,
            HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable => ObjectStoreErrorCode.Throttled,
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => ObjectStoreErrorCode.Timeout,
            HttpStatusCode.BadRequest or HttpStatusCode.RequestedRangeNotSatisfiable or HttpStatusCode.RequestEntityTooLarge => ObjectStoreErrorCode.InvalidRequest,
            >= HttpStatusCode.InternalServerError => ObjectStoreErrorCode.Unavailable,
            _ => ObjectStoreErrorCode.Unknown,
        };

        return (byStatus, detail);
    }
}
