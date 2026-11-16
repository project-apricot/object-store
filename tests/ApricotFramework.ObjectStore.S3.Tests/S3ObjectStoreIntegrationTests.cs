using System.Security.Cryptography;
using System.Text;
using Amazon.S3.Model;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.S3.Tests;

[Collection(S3ServerDefinition.Name)]
public class S3ObjectStoreIntegrationTests(S3ServerFixture server)
{
    /// <summary>
    /// The smallest part S3 allows, so a few megabytes exercise the multipart path.
    /// </summary>
    private const string SmallParts = "5242880";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MemoryStream Bytes(string text) => new(Encoding.UTF8.GetBytes(text));

    private static byte[] Random(int size)
    {
        var data = new byte[size];

        RandomNumberGenerator.Fill(data);

        return data;
    }

    private static async Task<byte[]> ReadAllAsync(StoredObject stored)
    {
        await using (stored)
        {
            using var buffer = new MemoryStream();

            await stored.Content.CopyToAsync(buffer, Ct);

            return buffer.ToArray();
        }
    }

    [Fact]
    public async Task Put_Get_Info_Exists_Delete_RoundTrip()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var put = await store.PutAsync("docs/a.txt", Bytes("hello"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            ContentDisposition = "attachment; filename=\"a.txt\"",
            CacheControl = "max-age=60",
            Metadata = new Dictionary<string, string> { ["Owner"] = "me" },
        }, Ct);

        Assert.Equal(5, put.Size);
        Assert.Equal("max-age=60", put.CacheControl);
        Assert.False(string.IsNullOrEmpty(put.ETag));

        var info = await store.GetInfoAsync("docs/a.txt", Ct);

        Assert.Equal(5, info.Size);
        Assert.Equal("text/plain", info.ContentType);
        Assert.Equal("max-age=60", info.CacheControl);
        Assert.Equal("me", info.Metadata["owner"]);
        Assert.NotNull(info.LastModified);

        var stored = await store.GetAsync("docs/a.txt", cancellationToken: Ct);

        Assert.Equal("text/plain", stored.Info.ContentType);
        Assert.Equal("attachment; filename=\"a.txt\"", stored.Info.ContentDisposition);
        Assert.Equal("max-age=60", stored.Info.CacheControl);
        Assert.Equal("hello", Encoding.UTF8.GetString(await ReadAllAsync(stored)));
        Assert.True(await store.ExistsAsync("docs/a.txt", Ct));

        await store.DeleteAsync("docs/a.txt", Ct);

        Assert.False(await store.ExistsAsync("docs/a.txt", Ct));
    }

    [Fact]
    public async Task Missing_Object_IsNotFound_WithTheSdkErrorInside()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var e = await Assert.ThrowsAsync<ObjectNotFoundException>(() => store.GetAsync("nope", cancellationToken: Ct));

        Assert.Equal("nope", e.Key);
        Assert.IsAssignableFrom<Amazon.Runtime.AmazonServiceException>(e.InnerException);
        Assert.Null(await store.TryGetAsync("nope", cancellationToken: Ct));
        await Assert.ThrowsAsync<ObjectNotFoundException>(() => store.GetInfoAsync("nope", Ct));
        Assert.False(await store.ExistsAsync("nope", Ct));
    }

    [Fact]
    public async Task Delete_Missing_IsIdempotent()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        await store.DeleteAsync("never-there", Ct);
    }

    [Fact]
    public async Task Get_Range_ReturnsTheSlice_AndTheWholeObjectsSize()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        await store.PutAsync("r", Bytes("0123456789"), cancellationToken: Ct);

        var middle = await store.GetAsync("r", new ObjectGetOptions { RangeStart = 2, RangeEnd = 5 }, Ct);

        Assert.Equal(10, middle.Info.Size);
        Assert.Equal("2345", Encoding.UTF8.GetString(await ReadAllAsync(middle)));

        var tail = await store.GetAsync("r", new ObjectGetOptions { RangeStart = 7 }, Ct);

        Assert.Equal("789", Encoding.UTF8.GetString(await ReadAllAsync(tail)));
    }

    [Fact]
    public async Task CreateOnly_SecondWriteIsAlreadyExists()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var options = new ObjectPutOptions { IfNoneMatch = "*" };

        await store.PutAsync("once", Bytes("one"), options, Ct);

        var e = await Assert.ThrowsAsync<ObjectAlreadyExistsException>(() => store.PutAsync("once", Bytes("two"), options, Ct));

        Assert.Equal("once", e.Key);
        Assert.Equal("one", Encoding.UTF8.GetString(await ReadAllAsync(await store.GetAsync("once", cancellationToken: Ct))));
    }

    [Fact]
    public async Task IfMatch_StaleETag_IsPreconditionFailed()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var first = await store.PutAsync("k", Bytes("one"), cancellationToken: Ct);

        await store.PutAsync("k", Bytes("two"), new ObjectPutOptions { IfMatch = first.ETag }, Ct);

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.PutAsync("k", Bytes("three"), new ObjectPutOptions { IfMatch = first.ETag }, Ct));

        Assert.Equal(ObjectStoreErrorCode.PreconditionFailed, e.ErrorCode);
    }

    [Fact]
    public async Task List_StripsThePrefix_AndPages()
    {
        var (store, _) = await server.CreateStoreAsync(s => s["Prefix"] = "svc");
        using var _s = store;

        for (var i = 0; i < 25; i++)
        {
            await store.PutAsync($"items/{i:D2}", Bytes("x"), cancellationToken: Ct);
        }

        await store.PutAsync("other/1", Bytes("x"), cancellationToken: Ct);

        var all = await store.ListAsync("items/", Ct).ToListAsync(Ct);

        Assert.Equal(25, all.Count);
        Assert.Equal("items/00", all[0].Key);
        Assert.Equal("items/24", all[^1].Key);
        Assert.All(all, i => Assert.Equal(1, i.Size));

        var seen = new List<string>();
        string? token = null;
        var pages = 0;

        do
        {
            var page = await store.ListPageAsync("items/", token, 10, Ct);

            seen.AddRange(page.Items.Select(i => i.Key));
            token = page.NextContinuationToken;
            pages++;
        }
        while (token is not null);

        Assert.Equal(3, pages);
        Assert.Equal(25, seen.Distinct().Count());
        Assert.Equal(26, await store.ListAsync(null, Ct).CountAsync(Ct));
    }

    [Fact]
    public async Task TwoStoresOnOneBucket_AreIsolatedByPrefix()
    {
        var bucket = await server.CreateBucketAsync();
        using var a = server.CreateStore(bucket, s => s["Prefix"] = "a", "a");
        using var b = server.CreateStore(bucket, s => s["Prefix"] = "b", "b");

        await a.PutAsync("same", Bytes("from-a"), cancellationToken: Ct);

        Assert.True(await a.ExistsAsync("same", Ct));
        Assert.False(await b.ExistsAsync("same", Ct));
        Assert.Empty(await b.ListAsync(null, Ct).ToListAsync(Ct));

        await b.PutAsync("same", Bytes("from-b"), cancellationToken: Ct);

        Assert.Equal("from-a", Encoding.UTF8.GetString(await ReadAllAsync(await a.GetAsync("same", cancellationToken: Ct))));

        // and the objects really are where the prefix says, so nothing is faking it
        using var raw = server.CreateRawClient();
        var keys = (await raw.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket }, Ct)).S3Objects.Select(o => o.Key).Order().ToList();

        Assert.Equal(["a/same", "b/same"], keys);
    }

    [Fact]
    public async Task Prefix_IsNotABoundaryTheCallerCanEscape()
    {
        var bucket = await server.CreateBucketAsync();
        using var inner = server.CreateStore(bucket, s => s["Prefix"] = "inner");

        await Assert.ThrowsAsync<ObjectStoreException>(() => inner.GetAsync("../secret", cancellationToken: Ct));
        await Assert.ThrowsAsync<ObjectStoreException>(() => inner.PutAsync("../secret", Bytes("x"), cancellationToken: Ct));
        await Assert.ThrowsAsync<ObjectStoreException>(() => inner.ListPageAsync("../", cancellationToken: Ct));
    }

    [Fact]
    public async Task DeleteMany_ReportsPerKey_AndHandlesMoreThanOneBatch()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var keys = Enumerable.Range(0, 1010).Select(i => $"bulk/{i}").ToList();

        await Parallel.ForEachAsync(keys, new ParallelOptions { MaxDegreeOfParallelism = 16, CancellationToken = Ct }, async (key, ct) =>
        {
            await store.PutAsync(key, Bytes("x"), cancellationToken: ct);
        });

        var result = await store.DeleteManyAsync([.. keys, "bulk/never-existed", "../bad", "bulk/0"], Ct);

        Assert.Equal(1011, result.Deleted.Count);
        Assert.DoesNotContain("bulk/0", result.Deleted.Skip(1010));

        var failure = Assert.Single(result.Failures);

        Assert.Equal("../bad", failure.Key);
        Assert.Equal(ObjectStoreErrorCode.InvalidKey, failure.ErrorCode);
        Assert.Empty(await store.ListAsync("bulk/", Ct).ToListAsync(Ct));
    }

    [Fact]
    public async Task Copy_KeepsContentTypeAndMetadata_OrReplacesBoth()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        await store.PutAsync("src", Bytes("data"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            ContentDisposition = "attachment",
            CacheControl = "max-age=60",
            Metadata = new Dictionary<string, string> { ["a"] = "1" },
        }, Ct);

        var kept = await store.CopyAsync("src", "kept", cancellationToken: Ct);

        Assert.Equal(4, kept.Size);
        Assert.Equal("text/plain", kept.ContentType);
        Assert.Equal("attachment", kept.ContentDisposition);
        Assert.Equal("max-age=60", kept.CacheControl);
        Assert.Equal("1", kept.Metadata["a"]);

        var replaced = await store.CopyAsync("src", "replaced", new ObjectCopyOptions { ContentType = "application/json" }, Ct);

        Assert.Equal("application/json", replaced.ContentType);
        Assert.Empty(replaced.Metadata);

        // naming any one of the four replaces all four, so what was not named is gone
        var stored = await store.GetInfoAsync("replaced", Ct);

        Assert.Null(stored.ContentDisposition);
        Assert.Null(stored.CacheControl);

        var onlyCache = await store.CopyAsync("src", "only-cache", new ObjectCopyOptions { CacheControl = "no-store" }, Ct);
        var onlyCacheStored = await store.GetInfoAsync("only-cache", Ct);

        Assert.Equal("no-store", onlyCache.CacheControl);
        Assert.Equal("no-store", onlyCacheStored.CacheControl);
        Assert.Null(onlyCacheStored.ContentDisposition);
        Assert.Empty(onlyCacheStored.Metadata);

        var withDisposition = await store.CopyAsync("src", "new-disposition", new ObjectCopyOptions { ContentDisposition = "inline" }, Ct);

        Assert.Equal("inline", (await store.GetInfoAsync("new-disposition", Ct)).ContentDisposition);
        Assert.Equal("inline", withDisposition.ContentDisposition);
        Assert.Equal("data", Encoding.UTF8.GetString(await ReadAllAsync(await store.GetAsync("replaced", cancellationToken: Ct))));
        Assert.True(await store.ExistsAsync("src", Ct));
    }

    [Fact]
    public async Task Copy_MissingSource_IsNotFound()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var e = await Assert.ThrowsAsync<ObjectNotFoundException>(() => store.CopyAsync("nope", "dst", cancellationToken: Ct));

        Assert.Equal("nope", e.Key);
    }

    [Fact]
    public async Task CopyTo_SameConnection_CopiesBetweenBuckets()
    {
        var (source, _) = await server.CreateStoreAsync();
        var (destination, destinationBucket) = await server.CreateStoreAsync();
        using var _s = source;
        using var _d = destination;

        await source.PutAsync("src", Bytes("payload"), new ObjectPutOptions { ContentType = "text/plain" }, Ct);

        var copy = await source.CopyToAsync("src", destination, "dst", cancellationToken: Ct);

        Assert.Equal(7, copy.Size);
        Assert.Equal("text/plain", copy.ContentType);
        Assert.Equal("payload", Encoding.UTF8.GetString(await ReadAllAsync(await destination.GetAsync("dst", cancellationToken: Ct))));

        using var raw = server.CreateRawClient();

        Assert.Single((await raw.ListObjectsV2Async(new ListObjectsV2Request { BucketName = destinationBucket }, Ct)).S3Objects);
    }

    [Fact]
    public async Task CopyTo_DifferentConnection_StreamsTheContent()
    {
        var (source, _) = await server.CreateStoreAsync();
        var destinationBucket = await server.CreateBucketAsync();

        // the same server by another name: a different connection as far as the store can tell, so the
        // copy cannot be left to the server and has to pass through here
        var alias = server.Endpoint!.Host == "localhost" ? "127.0.0.1" : "localhost";

        using var _s = source;
        using var destination = server.CreateStore(destinationBucket, s => s["ServiceUrl"] = $"http://{alias}:{server.Endpoint.Port}/");

        Assert.NotEqual(source.ConnectionKey, destination.ConnectionKey);

        await source.PutAsync("src", Bytes("streamed"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            ContentDisposition = "attachment",
            CacheControl = "max-age=60",
            Metadata = new Dictionary<string, string> { ["k"] = "v" },
        }, Ct);

        var copy = await source.CopyToAsync("src", destination, "dst", cancellationToken: Ct);

        Assert.Equal("text/plain", copy.ContentType);

        var landed = await destination.GetInfoAsync("dst", Ct);

        Assert.Equal("attachment", landed.ContentDisposition);
        Assert.Equal("max-age=60", landed.CacheControl);
        Assert.Equal("streamed", Encoding.UTF8.GetString(await ReadAllAsync(await destination.GetAsync("dst", cancellationToken: Ct))));
        Assert.Equal("v", (await destination.GetInfoAsync("dst", Ct)).Metadata["k"]);
    }

    [Fact]
    public async Task Put_LargeSeekableStream_GoesMultipart_AndRoundTrips()
    {
        var (store, _) = await server.CreateStoreAsync(s => s["MultipartPartSize"] = SmallParts);
        using var _s = store;

        var data = Random((11 * 1024 * 1024) + 123);

        var put = await store.PutAsync("big", new MemoryStream(data), new ObjectPutOptions { ContentType = "application/octet-stream" }, Ct);

        Assert.Equal(data.Length, put.Size);

        var info = await store.GetInfoAsync("big", Ct);

        Assert.Equal(data.Length, info.Size);
        Assert.Equal("application/octet-stream", info.ContentType);
        Assert.Equal(SHA256.HashData(data), SHA256.HashData(await ReadAllAsync(await store.GetAsync("big", cancellationToken: Ct))));
    }

    [Fact]
    public async Task Put_NonSeekableStream_LargeAndSmall_BothWork()
    {
        var (store, _) = await server.CreateStoreAsync(s => s["MultipartPartSize"] = SmallParts);
        using var _s = store;

        var big = Random((10 * 1024 * 1024) + 1);
        var small = Random(1000);
        var exact = Random(5 * 1024 * 1024);

        await store.PutAsync("big", new NonSeekableStream(new MemoryStream(big)), cancellationToken: Ct);
        await store.PutAsync("small", new NonSeekableStream(new MemoryStream(small)), cancellationToken: Ct);
        await store.PutAsync("exact", new NonSeekableStream(new MemoryStream(exact)), cancellationToken: Ct);
        await store.PutAsync("empty", new NonSeekableStream(new MemoryStream([])), cancellationToken: Ct);

        Assert.Equal(SHA256.HashData(big), SHA256.HashData(await ReadAllAsync(await store.GetAsync("big", cancellationToken: Ct))));
        Assert.Equal(SHA256.HashData(small), SHA256.HashData(await ReadAllAsync(await store.GetAsync("small", cancellationToken: Ct))));
        Assert.Equal(SHA256.HashData(exact), SHA256.HashData(await ReadAllAsync(await store.GetAsync("exact", cancellationToken: Ct))));
        Assert.Equal(0, (await store.GetInfoAsync("empty", Ct)).Size);
    }

    [Fact]
    public async Task Put_StreamThatFailsMidway_AbortsTheMultipartUpload_AndLeavesNoObject()
    {
        var (store, bucket) = await server.CreateStoreAsync(s => s["MultipartPartSize"] = SmallParts);
        using var _s = store;

        await Assert.ThrowsAsync<IOException>(() => store.PutAsync("doomed", new FailingStream(7 * 1024 * 1024), cancellationToken: Ct));

        Assert.False(await store.ExistsAsync("doomed", Ct));

        using var raw = server.CreateRawClient();
        var pending = await raw.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = bucket }, Ct);

        Assert.Empty(pending.MultipartUploads ?? []);
    }

    [Fact]
    public async Task ExplicitMultipart_StartUploadListComplete()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var one = Random(5 * 1024 * 1024);
        var two = Random(1234);

        var upload = await store.StartMultipartUploadAsync("mp", new MultipartUploadOptions
        {
            ContentType = "application/x-test",
            Metadata = new Dictionary<string, string> { ["m"] = "v" },
        }, Ct);

        var part2 = await store.UploadPartAsync(upload, 2, new MemoryStream(two), Ct);
        var part1 = await store.UploadPartAsync(upload, 1, new NonSeekableStream(new MemoryStream(one)), Ct);

        Assert.Equal(one.Length, part1.Size);

        var listed = await store.ListPartsAsync(upload, Ct);

        Assert.Equal([1, 2], listed.Select(p => p.PartNumber));
        Assert.False(await store.ExistsAsync("mp", Ct));

        var info = await store.CompleteMultipartUploadAsync(upload, [part1, part2], Ct);

        Assert.Equal(one.Length + two.Length, info.Size);
        Assert.Equal("application/x-test", info.ContentType);
        Assert.Equal("v", info.Metadata["m"]);
        Assert.Equal(SHA256.HashData([.. one, .. two]), SHA256.HashData(await ReadAllAsync(await store.GetAsync("mp", cancellationToken: Ct))));
    }

    [Fact]
    public async Task ExplicitMultipart_Abort_ThenTheUploadIsGone()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var upload = await store.StartMultipartUploadAsync("mp", cancellationToken: Ct);

        await store.UploadPartAsync(upload, 1, new MemoryStream(Random(100)), Ct);
        await store.AbortMultipartUploadAsync(upload, Ct);

        await Assert.ThrowsAsync<ObjectNotFoundException>(() => store.ListPartsAsync(upload, Ct));
        await Assert.ThrowsAsync<ObjectNotFoundException>(() => store.AbortMultipartUploadAsync(upload, Ct));
        Assert.False(await store.ExistsAsync("mp", Ct));
    }

    [Fact]
    public async Task ExplicitMultipart_BadInputs_AreInvalidRequests()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var upload = await store.StartMultipartUploadAsync("mp", cancellationToken: Ct);

        foreach (var number in new[] { 0, 10001 })
        {
            var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.UploadPartAsync(upload, number, new MemoryStream([1]), Ct));

            Assert.Equal(ObjectStoreErrorCode.InvalidRequest, e.ErrorCode);
        }

        var none = await Assert.ThrowsAsync<ObjectStoreException>(() => store.CompleteMultipartUploadAsync(upload, [], Ct));

        Assert.Equal(ObjectStoreErrorCode.InvalidRequest, none.ErrorCode);

        await store.AbortMultipartUploadAsync(upload, Ct);
    }

    [Fact]
    public async Task MultipartCopy_ForSourcesTooBigForOneRequest_CopiesInRanges()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var data = Random((11 * 1024 * 1024) + 7);

        await store.PutAsync("src", new MemoryStream(data), new ObjectPutOptions
        {
            ContentType = "application/x-src",
            ContentDisposition = "attachment; filename=\"big.bin\"",
            CacheControl = "max-age=3600",
            Metadata = new Dictionary<string, string> { ["a"] = "1" },
        }, Ct);

        var copy = await store.CopyMultipartAsync("src", store, "dst", null, 5 * 1024 * 1024, Ct);

        Assert.Equal(data.Length, copy.Size);

        var info = await store.GetInfoAsync("dst", Ct);

        Assert.Equal(data.Length, info.Size);
        Assert.Equal("application/x-src", info.ContentType);
        Assert.Equal("attachment; filename=\"big.bin\"", info.ContentDisposition);
        Assert.Equal("max-age=3600", info.CacheControl);
        Assert.Equal("1", info.Metadata["a"]);
        Assert.Equal(SHA256.HashData(data), SHA256.HashData(await ReadAllAsync(await store.GetAsync("dst", cancellationToken: Ct))));
    }

    [Fact]
    public async Task WrongSecret_IsAccessDenied()
    {
        var bucket = await server.CreateBucketAsync();
        using var store = server.CreateStore(bucket, s => s["SecretAccessKey"] = "definitely-not-the-secret");

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.GetInfoAsync("k", Ct));

        Assert.Equal(ObjectStoreErrorCode.AccessDenied, e.ErrorCode);
        Assert.DoesNotContain("definitely-not-the-secret", e.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("definitely-not-the-secret", e.InnerException?.Message ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingBucket_IsConfiguration()
    {
        server.Require();

        using var store = server.CreateStore("no-such-bucket-" + Guid.NewGuid().ToString("N")[..8]);

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.PutAsync("k", Bytes("x"), cancellationToken: Ct));

        Assert.Equal(ObjectStoreErrorCode.Configuration, e.ErrorCode);
    }

    [Fact]
    public async Task CallersCancellation_IsNotWrapped()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetInfoAsync("k", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.PutAsync("k", Bytes("x"), null, cts.Token));
    }

    [Fact]
    public async Task UnreachableServer_IsUnavailable_AndTransient()
    {
        server.Require();

        // nothing listens on port 1
        using var store = server.CreateStore("whatever", s => s["ServiceUrl"] = "http://127.0.0.1:1/");

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.GetInfoAsync("k", Ct));

        Assert.Equal(ObjectStoreErrorCode.Unavailable, e.ErrorCode);
        Assert.True(e.IsTransient());
    }

    [Fact]
    public async Task ConcurrentUse_OfOneStore_IsSafe()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        await Parallel.ForEachAsync(Enumerable.Range(0, 40), new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = Ct }, async (i, ct) =>
        {
            await store.PutAsync($"c/{i}", Bytes($"v{i}"), cancellationToken: ct);

            Assert.Equal($"v{i}", Encoding.UTF8.GetString(await ReadAllAsync(await store.GetAsync($"c/{i}", cancellationToken: ct))));
        });

        Assert.Equal(40, await store.ListAsync("c/", Ct).CountAsync(Ct));
    }
}
