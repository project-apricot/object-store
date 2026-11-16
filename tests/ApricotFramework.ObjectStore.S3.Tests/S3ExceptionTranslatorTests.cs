using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using Amazon.S3;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.S3.Impl;

namespace ApricotFramework.ObjectStore.S3.Tests;

public class S3ExceptionTranslatorTests
{
    private static AmazonS3Exception S3(HttpStatusCode status, string? code)
    {
        return new AmazonS3Exception("message mentioning bucket secret-bucket and key private/key", ErrorType.Unknown, code, "req", status);
    }

    private static ObjectStoreException Translate(Exception e, bool createOnly = false)
    {
        return S3ExceptionTranslator.Translate(e, "uploads", "the/key", createOnly);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NoSuchKey", ObjectStoreErrorCode.NotFound)]
    [InlineData(HttpStatusCode.NotFound, "NoSuchUpload", ObjectStoreErrorCode.NotFound)]
    [InlineData(HttpStatusCode.NotFound, null, ObjectStoreErrorCode.NotFound)]
    [InlineData(HttpStatusCode.NotFound, "NoSuchBucket", ObjectStoreErrorCode.Configuration)]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied", ObjectStoreErrorCode.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, "SignatureDoesNotMatch", ObjectStoreErrorCode.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, "InvalidAccessKeyId", ObjectStoreErrorCode.AccessDenied)]
    [InlineData(HttpStatusCode.BadRequest, "ExpiredToken", ObjectStoreErrorCode.AccessDenied)]
    [InlineData(HttpStatusCode.Forbidden, null, ObjectStoreErrorCode.AccessDenied)]
    [InlineData(HttpStatusCode.Unauthorized, null, ObjectStoreErrorCode.AccessDenied)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "SlowDown", ObjectStoreErrorCode.Throttled)]
    [InlineData(HttpStatusCode.TooManyRequests, null, ObjectStoreErrorCode.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, null, ObjectStoreErrorCode.Throttled)]
    [InlineData(HttpStatusCode.InternalServerError, "InternalError", ObjectStoreErrorCode.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, null, ObjectStoreErrorCode.Unavailable)]
    [InlineData(HttpStatusCode.RequestTimeout, "RequestTimeout", ObjectStoreErrorCode.Timeout)]
    [InlineData(HttpStatusCode.GatewayTimeout, null, ObjectStoreErrorCode.Timeout)]
    [InlineData(HttpStatusCode.BadRequest, "InvalidArgument", ObjectStoreErrorCode.InvalidRequest)]
    [InlineData(HttpStatusCode.BadRequest, "EntityTooLarge", ObjectStoreErrorCode.InvalidRequest)]
    [InlineData(HttpStatusCode.RequestedRangeNotSatisfiable, "InvalidRange", ObjectStoreErrorCode.InvalidRequest)]
    [InlineData(HttpStatusCode.BadRequest, null, ObjectStoreErrorCode.InvalidRequest)]
    [InlineData(HttpStatusCode.Conflict, "BucketAlreadyOwnedByYou", ObjectStoreErrorCode.Unknown)]
    [InlineData(HttpStatusCode.PreconditionFailed, "PreconditionFailed", ObjectStoreErrorCode.PreconditionFailed)]
    [InlineData(HttpStatusCode.PreconditionFailed, null, ObjectStoreErrorCode.PreconditionFailed)]
    public void Translate_S3Error_ByCodeThenStatus(HttpStatusCode status, string? vendorCode, ObjectStoreErrorCode expected)
    {
        Assert.Equal(expected, Translate(S3(status, vendorCode)).ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.PreconditionFailed, "PreconditionFailed")]
    [InlineData(HttpStatusCode.Conflict, "ConditionalRequestConflict")]
    public void Translate_ConditionalWriteThatLost_IsAlreadyExistsOnlyForCreateOnly(HttpStatusCode status, string code)
    {
        var createOnly = Translate(S3(status, code), createOnly: true);
        var other = Translate(S3(status, code), createOnly: false);

        Assert.IsType<ObjectAlreadyExistsException>(createOnly);
        Assert.Equal(ObjectStoreErrorCode.AlreadyExists, createOnly.ErrorCode);
        Assert.Equal(ObjectStoreErrorCode.PreconditionFailed, other.ErrorCode);
        Assert.IsNotType<ObjectAlreadyExistsException>(other);
    }

    [Fact]
    public void Translate_NotFound_IsAnObjectNotFoundException()
    {
        Assert.IsType<ObjectNotFoundException>(Translate(S3(HttpStatusCode.NotFound, "NoSuchKey")));
    }

    [Fact]
    public void Translate_KeepsTheSdkErrorAsInner_AndNamesStoreProviderAndKey()
    {
        var inner = S3(HttpStatusCode.NotFound, "NoSuchKey");
        var e = Translate(inner);

        Assert.Same(inner, e.InnerException);
        Assert.Equal("uploads", e.StoreName);
        Assert.Equal("s3", e.Provider);
        Assert.Equal("the/key", e.Key);
    }

    [Fact]
    public void Translate_Message_NeverRepeatsTheSdkMessage()
    {
        var e = Translate(S3(HttpStatusCode.NotFound, "NoSuchKey"));

        Assert.DoesNotContain("secret-bucket", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("private/key", e.Message, StringComparison.Ordinal);
        Assert.Contains("NoSuchKey", e.Message, StringComparison.Ordinal);
        Assert.Contains("404", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Translate_TimeoutCancellation_IsTimeout()
    {
        Assert.Equal(ObjectStoreErrorCode.Timeout, Translate(new TaskCanceledException()).ErrorCode);
        Assert.Equal(ObjectStoreErrorCode.Timeout, Translate(new AmazonClientException("x", new OperationCanceledException())).ErrorCode);
    }

    [Fact]
    public void Translate_NetworkFailures_AreUnavailable()
    {
        Assert.Equal(ObjectStoreErrorCode.Unavailable, Translate(new HttpRequestException()).ErrorCode);
        Assert.Equal(ObjectStoreErrorCode.Unavailable, Translate(new SocketException()).ErrorCode);
        Assert.Equal(ObjectStoreErrorCode.Unavailable, Translate(new AmazonClientException("x", new HttpRequestException())).ErrorCode);
    }

    [Fact]
    public void Translate_BareIOException_IsNotCalledUnavailable()
    {
        // far more likely to be the caller's own stream failing than the network, and a retry cannot fix it
        Assert.Equal(ObjectStoreErrorCode.Unknown, Translate(new IOException()).ErrorCode);
    }

    [Fact]
    public void Translate_Unrecognised_IsUnknown_AndDoesNotLeakTheMessage()
    {
        var e = Translate(new InvalidOperationException("contains endpoint http://internal:9000"));

        Assert.Equal(ObjectStoreErrorCode.Unknown, e.ErrorCode);
        Assert.DoesNotContain("internal", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NoSuchKey", ObjectStoreErrorCode.NotFound)]
    [InlineData("AccessDenied", ObjectStoreErrorCode.AccessDenied)]
    [InlineData("InternalError", ObjectStoreErrorCode.Unavailable)]
    [InlineData("SlowDown", ObjectStoreErrorCode.Throttled)]
    [InlineData("SomethingNew", ObjectStoreErrorCode.Unknown)]
    [InlineData(null, ObjectStoreErrorCode.Unknown)]
    public void ClassifyCode_ForBatchEntries(string? code, ObjectStoreErrorCode expected)
    {
        Assert.Equal(expected, S3ExceptionTranslator.ClassifyCode(code));
    }

    [Fact]
    public void IsTranslatable_OnlyStoreFailures()
    {
        using var cts = new CancellationTokenSource();

        Assert.True(S3ExceptionTranslator.IsTranslatable(S3(HttpStatusCode.NotFound, null), cts.Token));
        Assert.True(S3ExceptionTranslator.IsTranslatable(new HttpRequestException(), cts.Token));
        Assert.True(S3ExceptionTranslator.IsTranslatable(new TaskCanceledException(), cts.Token));
        Assert.False(S3ExceptionTranslator.IsTranslatable(new ObjectNotFoundException(), cts.Token));
        Assert.False(S3ExceptionTranslator.IsTranslatable(new ArgumentNullException("x"), cts.Token));
        Assert.False(S3ExceptionTranslator.IsTranslatable(new ObjectDisposedException("x"), cts.Token));
    }

    [Fact]
    public async Task IsTranslatable_TheCallersOwnCancellation_IsNot()
    {
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        Assert.False(S3ExceptionTranslator.IsTranslatable(new OperationCanceledException(cts.Token), cts.Token));
        Assert.True(S3ExceptionTranslator.IsTranslatable(new OperationCanceledException(), CancellationToken.None));
    }
}
