namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// A store is disposed by the factory that owns it and also by a container that handed it out, so doing it
/// twice has to be harmless. None of this needs a server.
/// </summary>
public class S3ObjectStoreDisposalTests
{
    private static S3ObjectStore Store(bool withPublicEndpoint)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Bucket"] = "bkt",
            ["ServiceUrl"] = "http://storage.internal:9000",
            ["AccessKeyId"] = "id",
            ["SecretAccessKey"] = "secret",
        };

        if (withPublicEndpoint)
        {
            settings["PublicServiceUrl"] = "https://files.example.com";
        }

        return new S3ObjectStore("s", S3StoreSettings.From(new ObjectStoreSettings(settings, "s")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispose_Twice_DoesNothingTheSecondTime(bool withPublicEndpoint)
    {
        var store = Store(withPublicEndpoint);

        store.Dispose();

        var second = Record.Exception(store.Dispose);

        Assert.Null(second);
    }

    [Fact]
    public async Task AfterDisposal_UseFailsLoudly_NotWithAStoreError()
    {
        var store = Store(withPublicEndpoint: false);

        store.Dispose();

        // a disposed client is a bug in the caller, not a failure of the store, so it is not translated
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => store.GetInfoAsync("k", TestContext.Current.CancellationToken));
    }
}
