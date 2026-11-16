using System.Text;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Options;

namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// The stores an application gets are the ones its factory makes, so the behavior that depends on what they
/// are has to hold for those and not only for ones built by hand.
/// </summary>
[Collection(S3ServerDefinition.Name)]
public class S3FactoryIntegrationTests(S3ServerFixture server)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ObjectStoreEntry Entry(string bucket)
    {
        var entry = new ObjectStoreEntry { Provider = S3ObjectStoreProvider.ProviderName };

        entry.Settings["Bucket"] = bucket;
        entry.Settings["ServiceUrl"] = server.Endpoint!.AbsoluteUri;
        entry.Settings["AccessKeyId"] = server.AccessKey;
        entry.Settings["SecretAccessKey"] = server.SecretKey;

        return entry;
    }

    [Fact]
    public async Task StoresFromTheFactory_AreTheRealS3Store_SoACopyBetweenThemCanHappenOnTheServer()
    {
        server.Require();

        var options = new ObjectStoreOptions();

        options.Stores["source"] = Entry(await server.CreateBucketAsync());
        options.Stores["target"] = Entry(await server.CreateBucketAsync());

        using var factory = new DefaultObjectStoreFactory([new S3ObjectStoreProvider()], options);

        var source = factory.GetStore("source");
        var target = factory.GetStore("target");

        // nothing sits in between that would hide the type from the check that allows a server-side copy
        var concrete = Assert.IsType<S3ObjectStore>(source);

        Assert.Equal(concrete.ConnectionKey, Assert.IsType<S3ObjectStore>(target).ConnectionKey);

        await source.PutAsync("k", new MemoryStream(Encoding.UTF8.GetBytes("payload")), new Model.ObjectPutOptions { ContentType = "text/plain" }, Ct);

        var copy = await source.CopyToAsync("k", target, "copied", cancellationToken: Ct);

        Assert.Equal(7, copy.Size);

        await using var stored = await target.GetAsync("copied", cancellationToken: Ct);

        Assert.Equal("text/plain", stored.Info.ContentType);
    }

    [Fact]
    public async Task TheFactory_BuildsEachClientOnce_AndDisposesItWhenItIsDisposed()
    {
        server.Require();

        var options = new ObjectStoreOptions();

        options.Stores["only"] = Entry(await server.CreateBucketAsync());

        var factory = new DefaultObjectStoreFactory([new S3ObjectStoreProvider()], options);
        var store = factory.GetStore("only");

        Assert.Same(store, factory.GetStore("ONLY"));

        factory.Dispose();

        // the client is gone with the factory that owned it, which is the point of it owning it
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => store.GetInfoAsync("k", Ct));
    }
}
