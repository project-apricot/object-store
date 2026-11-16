using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// S3 limits the key it receives to 1024 bytes, and what it receives is the store's prefix and the caller's key
/// together. Validation happens before anything is sent, so none of this needs a server.
/// </summary>
public class S3KeyLengthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static S3ObjectStore Store(string prefix)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Bucket"] = "bkt",
            ["ServiceUrl"] = "http://storage.internal:9000",
            ["AccessKeyId"] = "id",
            ["SecretAccessKey"] = "secret",
            ["Prefix"] = prefix,
        };

        return new S3ObjectStore("s", S3StoreSettings.From(new ObjectStoreSettings(settings, "s")));
    }

    [Fact]
    public async Task AKeyThatIsOnlyTooLongWithThePrefix_IsInvalidKey_NotARejectionFromS3()
    {
        // the prefix is 100 characters and a slash, so a key may be 923 bytes
        using var store = Store(new string('p', 100));

        var key = new string('k', 924);

        Assert.Null(ObjectKey.Explain(key));

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.GetInfoAsync(key, Ct));

        Assert.Equal(ObjectStoreErrorCode.InvalidKey, e.ErrorCode);
    }

    [Fact]
    public async Task EveryOperationThatTakesAKey_CountsThePrefix()
    {
        using var store = Store(new string('p', 100));

        var key = new string('k', 924);
        var upload = new MultipartUpload(key, "id");

        var calls = new Func<Task>[]
        {
            () => store.PutAsync(key, new MemoryStream([1]), cancellationToken: Ct),
            () => store.GetAsync(key, cancellationToken: Ct),
            () => store.GetInfoAsync(key, Ct),
            () => store.ExistsAsync(key, Ct),
            () => store.DeleteAsync(key, Ct),
            () => store.CopyAsync(key, "dst", cancellationToken: Ct),
            () => store.CopyAsync("src", key, cancellationToken: Ct),
            () => store.CreateDownloadUrlAsync(key, cancellationToken: Ct),
            () => store.CreateUploadUrlAsync(key, cancellationToken: Ct),
            () => store.CreateUploadFormAsync(key, cancellationToken: Ct),
            () => store.StartMultipartUploadAsync(key, cancellationToken: Ct),
            () => store.UploadPartAsync(upload, 1, new MemoryStream([1]), Ct),
            () => store.CreatePartUploadUrlAsync(upload, 1, cancellationToken: Ct),
            () => store.ListPartsAsync(upload, Ct),
            () => store.CompleteMultipartUploadAsync(upload, [new UploadedPart(1, "\"e\"")], Ct),
            () => store.AbortMultipartUploadAsync(upload, Ct),
        };

        foreach (var call in calls)
        {
            var e = await Assert.ThrowsAsync<ObjectStoreException>(call);

            Assert.Equal(ObjectStoreErrorCode.InvalidKey, e.ErrorCode);
        }
    }

    [Fact]
    public async Task AKeyThatJustFits_IsAccepted()
    {
        using var store = Store(new string('p', 100));

        // 101 bytes of prefix leave 923, and signing is local so a URL proves the key was accepted
        var request = await store.CreateDownloadUrlAsync(new string('k', 923), cancellationToken: Ct);

        Assert.NotNull(request.Url);
    }

    [Fact]
    public async Task WithNoPrefix_TheWholeLimitIsTheCallers()
    {
        using var store = Store(string.Empty);

        Assert.NotNull((await store.CreateDownloadUrlAsync(new string('k', 1024), cancellationToken: Ct)).Url);

        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateDownloadUrlAsync(new string('k', 1025), cancellationToken: Ct));
    }
}
