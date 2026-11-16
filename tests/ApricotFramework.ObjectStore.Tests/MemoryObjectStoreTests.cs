using System.Text;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.Tests;

public class MemoryObjectStoreTests
{
    /// <summary>
    /// The store under test.
    /// </summary>
    private readonly MemoryObjectStore store = new("test");

    private static MemoryStream Bytes(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }

    private static async Task<string> ReadAsync(StoredObject stored)
    {
        await using (stored)
        {
            using var reader = new StreamReader(stored.Content, Encoding.UTF8);

            return await reader.ReadToEndAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Put_ThenGet_RoundTripsContentAndDescription()
    {
        var info = await this.store.PutAsync("a/b.txt", Bytes("hello"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string> { ["Owner"] = "me" },
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("a/b.txt", info.Key);
        Assert.Equal(5, info.Size);
        Assert.NotNull(info.ETag);
        Assert.NotNull(info.LastModified);

        var stored = await this.store.GetAsync("a/b.txt", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("text/plain", stored.Info.ContentType);
        Assert.Equal("me", stored.Info.Metadata["owner"]);
        Assert.Equal("hello", await ReadAsync(stored));
    }

    [Fact]
    public async Task Put_Existing_Replaces()
    {
        await this.store.PutAsync("k", Bytes("one"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.PutAsync("k", Bytes("two"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("two", await ReadAsync(await this.store.GetAsync("k", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Put_ReadsTheStreamFromItsCurrentPosition()
    {
        var content = Bytes("skip-keep");

        content.Position = 5;

        await this.store.PutAsync("k", content, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("keep", await ReadAsync(await this.store.GetAsync("k", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Put_SameContent_SameETag_DifferentContent_DifferentETag()
    {
        var a = await this.store.PutAsync("a", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        var b = await this.store.PutAsync("b", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        var c = await this.store.PutAsync("c", Bytes("y"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(a.ETag, b.ETag);
        Assert.NotEqual(a.ETag, c.ETag);
    }

    [Fact]
    public async Task Put_CreateOnly_ExistingObject_ThrowsAlreadyExists()
    {
        await this.store.PutAsync("k", Bytes("one"), cancellationToken: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ObjectAlreadyExistsException>(() => this.store.PutAsync("k", Bytes("two"), new ObjectPutOptions { IfNoneMatch = "*" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("k", e.Key);
        Assert.Equal("one", await ReadAsync(await this.store.GetAsync("k", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Put_CreateOnly_NewObject_Succeeds()
    {
        await this.store.PutAsync("k", Bytes("one"), new ObjectPutOptions { IfNoneMatch = "*" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await this.store.ExistsAsync("k", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Put_IfMatch_MatchingETag_Succeeds_StaleETag_Fails()
    {
        var first = await this.store.PutAsync("k", Bytes("one"), cancellationToken: TestContext.Current.CancellationToken);

        await this.store.PutAsync("k", Bytes("two"), new ObjectPutOptions { IfMatch = first.ETag }, cancellationToken: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.PutAsync("k", Bytes("three"), new ObjectPutOptions { IfMatch = first.ETag }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjectStoreErrorCode.PreconditionFailed, e.ErrorCode);
    }

    [Fact]
    public async Task Put_IfMatch_MissingObject_FailsThePrecondition()
    {
        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.PutAsync("k", Bytes("x"), new ObjectPutOptions { IfMatch = "\"abc\"" }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjectStoreErrorCode.PreconditionFailed, e.ErrorCode);
    }

    [Fact]
    public async Task Put_IfNoneMatch_SpecificETag_FailsOnlyWhenItMatches()
    {
        var first = await this.store.PutAsync("k", Bytes("one"), cancellationToken: TestContext.Current.CancellationToken);

        await this.store.PutAsync("k", Bytes("two"), new ObjectPutOptions { IfNoneMatch = "\"other\"" }, cancellationToken: TestContext.Current.CancellationToken);

        var current = await this.store.GetInfoAsync("k", cancellationToken: TestContext.Current.CancellationToken);
        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.PutAsync("k", Bytes("three"), new ObjectPutOptions { IfNoneMatch = current.ETag }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjectStoreErrorCode.PreconditionFailed, e.ErrorCode);
        Assert.NotEqual(first.ETag, current.ETag);
    }

    [Fact]
    public async Task Put_InvalidKey_ThrowsInvalidKey()
    {
        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.PutAsync("../x", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjectStoreErrorCode.InvalidKey, e.ErrorCode);
        Assert.Equal("test", e.StoreName);
    }

    [Fact]
    public async Task Put_NullContent_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => this.store.PutAsync("k", null!, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Get_Missing_ThrowsNotFoundWithContext()
    {
        var e = await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.GetAsync("nope", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("nope", e.Key);
        Assert.Equal("test", e.StoreName);
        Assert.Equal("memory", e.Provider);
    }

    [Fact]
    public async Task TryGet_Missing_ReturnsNull()
    {
        Assert.Null(await this.store.TryGetAsync("nope", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetInfo_Missing_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.GetInfoAsync("nope", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0L, 4L, "01234")]
    [InlineData(2L, 5L, "2345")]
    [InlineData(7L, null, "789")]
    [InlineData(null, 2L, "012")]
    [InlineData(5L, 500L, "56789")]
    public async Task Get_Range_ReturnsTheSliceAndTheWholeObjectsSize(long? start, long? end, string expected)
    {
        await this.store.PutAsync("k", Bytes("0123456789"), cancellationToken: TestContext.Current.CancellationToken);

        var stored = await this.store.GetAsync("k", new ObjectGetOptions { RangeStart = start, RangeEnd = end }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(10, stored.Info.Size);
        Assert.Equal(expected, await ReadAsync(stored));
    }

    [Theory]
    [InlineData(10L, null)]
    [InlineData(-1L, 3L)]
    [InlineData(5L, 2L)]
    public async Task Get_UnsatisfiableRange_ThrowsInvalidRequest(long start, long? end)
    {
        await this.store.PutAsync("k", Bytes("0123456789"), cancellationToken: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.GetAsync("k", new ObjectGetOptions { RangeStart = start, RangeEnd = end }, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjectStoreErrorCode.InvalidRequest, e.ErrorCode);
    }

    [Fact]
    public async Task Get_ContentIsNotWritable_SoCallersCannotCorruptTheStore()
    {
        await this.store.PutAsync("k", Bytes("abc"), cancellationToken: TestContext.Current.CancellationToken);

        await using var stored = await this.store.GetAsync("k", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(stored.Content.CanWrite);
    }

    [Fact]
    public async Task Exists_ReflectsPresence()
    {
        Assert.False(await this.store.ExistsAsync("k", cancellationToken: TestContext.Current.CancellationToken));

        await this.store.PutAsync("k", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await this.store.ExistsAsync("k", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delete_RemovesAndIsIdempotent()
    {
        await this.store.PutAsync("k", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.DeleteAsync("k", cancellationToken: TestContext.Current.CancellationToken);
        await this.store.DeleteAsync("k", cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await this.store.ExistsAsync("k", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteMany_ReportsDeletedAndInvalidKeysSeparately()
    {
        await this.store.PutAsync("a", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.PutAsync("b", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);

        var result = await this.store.DeleteManyAsync(["a", "b", "never-existed", "../bad"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(["a", "b", "never-existed"], result.Deleted);

        var failure = Assert.Single(result.Failures);

        Assert.Equal("../bad", failure.Key);
        Assert.Equal(ObjectStoreErrorCode.InvalidKey, failure.ErrorCode);
        Assert.False(await this.store.ExistsAsync("a", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteMany_AllValid_Succeeded()
    {
        Assert.True((await this.store.DeleteManyAsync(["a"], cancellationToken: TestContext.Current.CancellationToken)).Succeeded);
    }

    [Fact]
    public async Task List_ReturnsKeysInOrdinalOrderFilteredByPrefix()
    {
        foreach (var key in new[] { "b/2", "a/1", "b/1", "c", "B/x" })
        {
            await this.store.PutAsync(key, Bytes(key), cancellationToken: TestContext.Current.CancellationToken);
        }

        var all = await this.store.ListAsync(cancellationToken: TestContext.Current.CancellationToken).Select(i => i.Key).ToListAsync(TestContext.Current.CancellationToken);
        var b = await this.store.ListAsync("b/", cancellationToken: TestContext.Current.CancellationToken).Select(i => i.Key).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["B/x", "a/1", "b/1", "b/2", "c"], all);
        Assert.Equal(["b/1", "b/2"], b);
    }

    [Fact]
    public async Task ListPage_PagesWithoutGapsOrDuplicates()
    {
        for (var i = 0; i < 7; i++)
        {
            await this.store.PutAsync($"k{i}", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        }

        var seen = new List<string>();
        string? token = null;
        var pages = 0;

        do
        {
            var page = await this.store.ListPageAsync(null, token, 3, cancellationToken: TestContext.Current.CancellationToken);

            seen.AddRange(page.Items.Select(i => i.Key));
            token = page.NextContinuationToken;
            pages++;
        }
        while (token is not null);

        Assert.Equal(3, pages);
        Assert.Equal(["k0", "k1", "k2", "k3", "k4", "k5", "k6"], seen);
    }

    [Fact]
    public async Task ListPage_ExactlyOnePageFull_HasNoNextToken()
    {
        await this.store.PutAsync("a", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.PutAsync("b", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null((await this.store.ListPageAsync(null, null, 2, cancellationToken: TestContext.Current.CancellationToken)).NextContinuationToken);
    }

    [Fact]
    public async Task ListPage_PrefixIsNotAFolderBoundary()
    {
        await this.store.PutAsync("avatars/1", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.PutAsync("avatars-old/1", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);

        var page = await this.store.ListPageAsync("avatars", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, page.Items.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task ListPage_PageSizeOutOfRange_Throws(int size)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => this.store.ListPageAsync(null, null, size, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListPage_InvalidPrefix_Throws()
    {
        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.ListPageAsync("../", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjectStoreErrorCode.InvalidKey, e.ErrorCode);
    }

    [Fact]
    public async Task Copy_KeepsContentTypeAndMetadataByDefault()
    {
        await this.store.PutAsync("src", Bytes("data"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string> { ["a"] = "1" },
        }, cancellationToken: TestContext.Current.CancellationToken);

        var copy = await this.store.CopyAsync("src", "dst", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("dst", copy.Key);
        Assert.Equal("text/plain", copy.ContentType);
        Assert.Equal("1", copy.Metadata["a"]);
        Assert.Equal("data", await ReadAsync(await this.store.GetAsync("dst", cancellationToken: TestContext.Current.CancellationToken)));
        Assert.True(await this.store.ExistsAsync("src", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Copy_WithNewContentType_ReplacesBothContentTypeAndMetadata()
    {
        await this.store.PutAsync("src", Bytes("data"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string> { ["a"] = "1" },
        }, cancellationToken: TestContext.Current.CancellationToken);

        var copy = await this.store.CopyAsync("src", "dst", new ObjectCopyOptions { ContentType = "application/json" }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("application/json", copy.ContentType);
        Assert.Empty(copy.Metadata);
    }

    [Fact]
    public async Task Copy_MissingSource_ThrowsNotFound()
    {
        var e = await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.CopyAsync("nope", "dst", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("nope", e.Key);
    }

    [Fact]
    public async Task CopyTo_AnotherStore_CopiesTheContent()
    {
        var other = new MemoryObjectStore("other");

        await this.store.PutAsync("src", Bytes("data"), new ObjectPutOptions { ContentType = "text/plain" }, cancellationToken: TestContext.Current.CancellationToken);

        var copy = await this.store.CopyToAsync("src", other, "dst", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("text/plain", copy.ContentType);
        Assert.Equal("data", await ReadAsync(await other.GetAsync("dst", cancellationToken: TestContext.Current.CancellationToken)));
        Assert.False(await this.store.ExistsAsync("dst", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CopyTo_SameStore_IsACopy()
    {
        await this.store.PutAsync("src", Bytes("data"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.CopyToAsync("src", this.store, "dst", cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await this.store.ExistsAsync("dst", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EveryFailure_ComesThroughTheTask_NotAtTheCallSite()
    {
        var missing = new MultipartUpload("k", "no-such-upload");

        var calls = new Func<Task>[]
        {
            () => this.store.GetAsync(string.Empty, cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.GetAsync("nope", cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.GetInfoAsync("../x", TestContext.Current.CancellationToken),
            () => this.store.ExistsAsync("/x", TestContext.Current.CancellationToken),
            () => this.store.DeleteAsync(string.Empty, TestContext.Current.CancellationToken),
            () => this.store.DeleteManyAsync(null!, TestContext.Current.CancellationToken),
            () => this.store.ListPageAsync("../", cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.ListPageAsync(null, null, 0, TestContext.Current.CancellationToken),
            () => this.store.CopyAsync("nope", "dst", cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.CopyToAsync("a", null!, "b", cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.StartMultipartUploadAsync(string.Empty, cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.ListPartsAsync(missing, TestContext.Current.CancellationToken),
            () => this.store.AbortMultipartUploadAsync(missing, TestContext.Current.CancellationToken),
            () => this.store.CompleteMultipartUploadAsync(missing, [new UploadedPart(1, "\"x\"")], TestContext.Current.CancellationToken),
        };

        foreach (var call in calls)
        {
            Task? task = null;

            // a caller that holds the task and awaits it later must see the failure there, as with a real provider
            var thrownAtTheCall = Record.Exception(() =>
            {
                task = call();
            });

            Assert.Null(thrownAtTheCall);
            Assert.True(task!.IsFaulted);
            await Assert.ThrowsAnyAsync<Exception>(() => task);
        }
    }

    [Fact]
    public async Task ACanceledToken_GivesACanceledTask()
    {
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        var task = this.store.GetInfoAsync("k", cts.Token);

        Assert.True(task.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task DeleteMany_AKeyNamedTwice_IsDeletedAndReportedOnce()
    {
        await this.store.PutAsync("a", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);

        var result = await this.store.DeleteManyAsync(["a", "a", "b", "a", "b"], TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], result.Deleted);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task List_StaysConsistentThroughOverwritesAndDeletes()
    {
        foreach (var key in new[] { "a", "b", "c", "d" })
        {
            await this.store.PutAsync(key, Bytes("1"), cancellationToken: TestContext.Current.CancellationToken);
        }

        await this.store.PutAsync("b", Bytes("22"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.DeleteAsync("c", TestContext.Current.CancellationToken);
        await this.store.CopyAsync("a", "e", cancellationToken: TestContext.Current.CancellationToken);

        var listed = await this.store.ListAsync(cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "d", "e"], listed.Select(i => i.Key));
        Assert.Equal(2, listed.Single(i => i.Key == "b").Size);
    }

    [Fact]
    public async Task ListPage_FromATokenAtTheLastKey_IsEmpty_AndAPrefixBeyondEverythingFindsNothing()
    {
        await this.store.PutAsync("a", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.PutAsync("b", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty((await this.store.ListPageAsync(null, "b", 10, TestContext.Current.CancellationToken)).Items);
        Assert.Empty((await this.store.ListPageAsync("z", null, 10, TestContext.Current.CancellationToken)).Items);
        Assert.Empty((await new MemoryObjectStore("empty").ListPageAsync(null, null, 10, TestContext.Current.CancellationToken)).Items);
    }

    [Fact]
    public async Task ListPage_ATokenBeforeThePrefix_StartsAtThePrefix()
    {
        foreach (var key in new[] { "a/1", "b/1", "b/2", "c/1" })
        {
            await this.store.PutAsync(key, Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        }

        var page = await this.store.ListPageAsync("b/", "a/1", 10, TestContext.Current.CancellationToken);

        Assert.Equal(["b/1", "b/2"], page.Items.Select(i => i.Key));
    }

    [Fact]
    public async Task ListPage_KeysThatSortAfterTheTokenByTheNextCharacter_AreNotSkipped()
    {
        // the token "a" is followed by "a\0" in order; "a0" and "aa" come after, and must still be found
        foreach (var key in new[] { "a", "a0", "aa", "b" })
        {
            await this.store.PutAsync(key, Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        }

        var page = await this.store.ListPageAsync(null, "a", 10, TestContext.Current.CancellationToken);

        Assert.Equal(["a0", "aa", "b"], page.Items.Select(i => i.Key));
    }

    [Fact]
    public async Task ListAsync_OverManyKeys_ReturnsEachOnceInOrder()
    {
        for (var i = 0; i < 2500; i++)
        {
            await this.store.PutAsync($"k{i:D5}", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);
        }

        var keys = (await this.store.ListAsync(cancellationToken: TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken)).Select(i => i.Key).ToList();

        Assert.Equal(2500, keys.Count);
        Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        Assert.Equal(2500, keys.Distinct().Count());
    }

    [Fact]
    public async Task ContentDispositionAndCacheControl_RoundTrip_AndSurviveACopy()
    {
        await this.store.PutAsync("src", Bytes("data"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            ContentDisposition = "attachment; filename=\"a.txt\"",
            CacheControl = "max-age=60",
            Metadata = new Dictionary<string, string> { ["a"] = "1" },
        }, TestContext.Current.CancellationToken);

        var info = await this.store.GetInfoAsync("src", TestContext.Current.CancellationToken);

        Assert.Equal("attachment; filename=\"a.txt\"", info.ContentDisposition);
        Assert.Equal("max-age=60", info.CacheControl);

        var copy = await this.store.CopyAsync("src", "dst", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("attachment; filename=\"a.txt\"", copy.ContentDisposition);
        Assert.Equal("max-age=60", copy.CacheControl);
        Assert.Equal("1", copy.Metadata["a"]);

        var other = new MemoryObjectStore("other");
        var streamed = await this.store.CopyToAsync("src", other, "dst", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("attachment; filename=\"a.txt\"", streamed.ContentDisposition);
        Assert.Equal("max-age=60", streamed.CacheControl);
        Assert.Equal("text/plain", streamed.ContentType);
    }

    [Fact]
    public async Task Copy_NamingAnyOneOfTheFour_ReplacesAllFour()
    {
        await this.store.PutAsync("src", Bytes("data"), new ObjectPutOptions
        {
            ContentType = "text/plain",
            ContentDisposition = "attachment",
            CacheControl = "max-age=60",
            Metadata = new Dictionary<string, string> { ["a"] = "1" },
        }, TestContext.Current.CancellationToken);

        var copy = await this.store.CopyAsync("src", "dst", new ObjectCopyOptions { CacheControl = "no-store" }, TestContext.Current.CancellationToken);

        Assert.Equal("no-store", copy.CacheControl);
        Assert.Null(copy.ContentType);
        Assert.Null(copy.ContentDisposition);
        Assert.Empty(copy.Metadata);
        Assert.True(new ObjectCopyOptions { ContentDisposition = "x" }.ReplacesMetadata);
        Assert.False(new ObjectCopyOptions().ReplacesMetadata);
    }

    [Fact]
    public async Task Multipart_CarriesTheHeadersItWasStartedWith()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", new MultipartUploadOptions { ContentDisposition = "attachment", CacheControl = "no-cache" }, TestContext.Current.CancellationToken);
        var part = await this.store.UploadPartAsync(upload, 1, Bytes("A"), TestContext.Current.CancellationToken);

        var info = await this.store.CompleteMultipartUploadAsync(upload, [part], TestContext.Current.CancellationToken);

        Assert.Equal("attachment", info.ContentDisposition);
        Assert.Equal("no-cache", info.CacheControl);
    }

    [Fact]
    public async Task Presigning_IsNotSupported_AndFailsTheTaskRatherThanThrowing()
    {
        var calls = new Func<Task>[]
        {
            () => this.store.CreateDownloadUrlAsync("k", cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.CreateUploadUrlAsync("k", cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.CreateUploadFormAsync("k", cancellationToken: TestContext.Current.CancellationToken),
            () => this.store.CreatePartUploadUrlAsync(new MultipartUpload("k", "id"), 1, cancellationToken: TestContext.Current.CancellationToken),
        };

        foreach (var call in calls)
        {
            var task = call();
            var e = await Assert.ThrowsAsync<ObjectStoreException>(() => task);

            Assert.Equal(ObjectStoreErrorCode.NotSupported, e.ErrorCode);
        }
    }

    [Fact]
    public async Task Multipart_EndToEnd_AssemblesPartsInOrder()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", new MultipartUploadOptions
        {
            ContentType = "application/octet-stream",
            Metadata = new Dictionary<string, string> { ["m"] = "v" },
        }, cancellationToken: TestContext.Current.CancellationToken);

        // uploaded out of order on purpose; completion order is what counts
        var second = await this.store.UploadPartAsync(upload, 2, Bytes("BBB"), cancellationToken: TestContext.Current.CancellationToken);
        var first = await this.store.UploadPartAsync(upload, 1, Bytes("AAA"), cancellationToken: TestContext.Current.CancellationToken);

        var listed = await this.store.ListPartsAsync(upload, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], listed.Select(p => p.PartNumber));
        Assert.Equal(3, first.Size);

        var info = await this.store.CompleteMultipartUploadAsync(upload, [first, second], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(6, info.Size);
        Assert.Equal("application/octet-stream", info.ContentType);
        Assert.Equal("v", info.Metadata["m"]);
        Assert.Equal("AAABBB", await ReadAsync(await this.store.GetAsync("big", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Multipart_NotVisibleUntilCompleted()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", cancellationToken: TestContext.Current.CancellationToken);

        await this.store.UploadPartAsync(upload, 1, Bytes("A"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(await this.store.ExistsAsync("big", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Multipart_ReUploadingAPart_ReplacesIt()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", cancellationToken: TestContext.Current.CancellationToken);

        await this.store.UploadPartAsync(upload, 1, Bytes("old"), cancellationToken: TestContext.Current.CancellationToken);

        var replaced = await this.store.UploadPartAsync(upload, 1, Bytes("new"), cancellationToken: TestContext.Current.CancellationToken);

        await this.store.CompleteMultipartUploadAsync(upload, [replaced], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("new", await ReadAsync(await this.store.GetAsync("big", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task Multipart_Complete_ChecksPartsAndOrder()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", cancellationToken: TestContext.Current.CancellationToken);
        var one = await this.store.UploadPartAsync(upload, 1, Bytes("A"), cancellationToken: TestContext.Current.CancellationToken);
        var two = await this.store.UploadPartAsync(upload, 2, Bytes("B"), cancellationToken: TestContext.Current.CancellationToken);

        async Task Fails(IEnumerable<UploadedPart> parts)
        {
            var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.CompleteMultipartUploadAsync(upload, parts, cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ObjectStoreErrorCode.InvalidRequest, e.ErrorCode);
        }

        await Fails([]);
        await Fails([two, one]);
        await Fails([one, one]);
        await Fails([one, new UploadedPart(3, "\"x\"")]);
        await Fails([one with { ETag = "\"wrong\"" }]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10001)]
    public async Task Multipart_PartNumberOutOfRange_Throws(int number)
    {
        var upload = await this.store.StartMultipartUploadAsync("big", cancellationToken: TestContext.Current.CancellationToken);

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => this.store.UploadPartAsync(upload, number, Bytes("A"), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjectStoreErrorCode.InvalidRequest, e.ErrorCode);
    }

    [Fact]
    public async Task Multipart_Abort_DiscardsTheUpload()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", cancellationToken: TestContext.Current.CancellationToken);

        await this.store.UploadPartAsync(upload, 1, Bytes("A"), cancellationToken: TestContext.Current.CancellationToken);
        await this.store.AbortMultipartUploadAsync(upload, cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.ListPartsAsync(upload, cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.AbortMultipartUploadAsync(upload, cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(await this.store.ExistsAsync("big", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Multipart_UnknownOrMismatchedUpload_IsNotFound()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.UploadPartAsync(new MultipartUpload("big", "bogus"), 1, Bytes("A"), cancellationToken: TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.UploadPartAsync(upload with { Key = "other" }, 1, Bytes("A"), cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Multipart_CompletedUpload_CannotBeCompletedAgain()
    {
        var upload = await this.store.StartMultipartUploadAsync("big", cancellationToken: TestContext.Current.CancellationToken);
        var part = await this.store.UploadPartAsync(upload, 1, Bytes("A"), cancellationToken: TestContext.Current.CancellationToken);

        await this.store.CompleteMultipartUploadAsync(upload, [part], cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<ObjectNotFoundException>(() => this.store.CompleteMultipartUploadAsync(upload, [part], cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancellation_Throws_OperationCanceled_NotAWrappedException()
    {
        using var cts = new CancellationTokenSource();

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => this.store.GetInfoAsync("k", cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => this.store.PutAsync("k", Bytes("x"), null, cts.Token));
    }

    [Fact]
    public async Task ConcurrentWrites_AreAllKept()
    {
        await Task.WhenAll(Enumerable.Range(0, 200).Select(i => this.store.PutAsync($"k{i}", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken)));

        Assert.Equal(200, await this.store.ListAsync(cancellationToken: TestContext.Current.CancellationToken).CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LastModified_UsesTheTimeProvider()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var timed = new MemoryObjectStore("t", clock);

        var info = await timed.PutAsync("k", Bytes("x"), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(clock.GetUtcNow(), info.LastModified);
    }
}
