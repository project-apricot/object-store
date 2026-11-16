using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Options;

namespace ApricotFramework.ObjectStore.Tests;

public class FactoryTests
{
    private static ObjectStoreOptions Declare(params (string Name, string Provider)[] stores)
    {
        var options = new ObjectStoreOptions();

        foreach (var (name, provider) in stores)
        {
            options.Stores[name] = new ObjectStoreEntry { Provider = provider };
        }

        return options;
    }

    [Fact]
    public void GetStore_ReturnsTheSameInstanceEachTime()
    {
        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("uploads", "memory")));

        Assert.Same(factory.GetStore("uploads"), factory.GetStore("UPLOADS"));
    }

    [Fact]
    public void GetStore_CarriesTheConfiguredSpellingOfTheName()
    {
        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("Uploads", "memory")));

        Assert.Equal("Uploads", factory.GetStore("uploads").Name);
    }

    [Fact]
    public void GetStore_Unknown_ThrowsStoreNotFound()
    {
        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("uploads", "memory")));

        var e = Assert.Throws<ObjectStoreException>(() => factory.GetStore("nope"));

        Assert.Equal(ObjectStoreErrorCode.StoreNotFound, e.ErrorCode);
    }

    [Fact]
    public void TryGetStore_Unknown_ReturnsFalse()
    {
        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("uploads", "memory")));

        Assert.False(factory.TryGetStore("nope", out var store));
        Assert.Null(store);
    }

    [Fact]
    public void GetStore_UnregisteredProvider_ThrowsConfigurationNamingTheRegisteredOnes()
    {
        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("uploads", "s3")));

        var e = Assert.Throws<ObjectStoreConfigurationException>(() => factory.GetStore("uploads"));

        Assert.Contains("memory", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetStore_NoProviderAtAll_SaysSo()
    {
        using var factory = new DefaultObjectStoreFactory([], Declare(("uploads", "s3")));

        var e = Assert.Throws<ObjectStoreConfigurationException>(() => factory.GetStore("uploads"));

        Assert.Contains("no provider is registered", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDefaultStore_UsesConfiguredName()
    {
        var options = Declare(("a", "memory"), ("b", "memory"));

        options.DefaultStore = " b ";

        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], options);

        Assert.Equal("b", factory.GetDefaultStore().Name);
    }

    [Fact]
    public void GetDefaultStore_FallsBackToDefaultName()
    {
        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("default", "memory")));

        Assert.Equal("default", factory.GetDefaultStore().Name);
    }

    [Fact]
    public void GetDefaultStore_NothingNamedDefault_Throws()
    {
        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("other", "memory")));

        Assert.Throws<ObjectStoreException>(() => factory.GetDefaultStore());
    }

    [Fact]
    public void Sources_AreAskedBeforeConfiguration()
    {
        var source = new FixedDefinitionSource(new ObjectStoreDefinition { Name = "uploads", Provider = "memory" });
        var provider = new MemoryObjectStoreProvider();

        // configuration names a provider that is not registered; the source's answer must win
        using var factory = new DefaultObjectStoreFactory([provider], Declare(("uploads", "s3")), [source]);

        Assert.Equal("memory", factory.GetStore("uploads").Provider);
    }

    [Fact]
    public void Sources_FirstOneHoldingTheNameWins()
    {
        var first = new FixedDefinitionSource(new ObjectStoreDefinition { Name = "x", Provider = "memory" });
        var second = new FixedDefinitionSource(new ObjectStoreDefinition { Name = "x", Provider = "other" });

        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], null, [first, second]);

        Assert.Equal("memory", factory.GetStore("x").Provider);
        Assert.Equal(0, second.Asked);
    }

    [Fact]
    public void Sources_AreAskedOncePerStore_NotOncePerLookup()
    {
        var source = new FixedDefinitionSource(new ObjectStoreDefinition { Name = "x", Provider = "memory" });

        using var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], null, [source]);

        factory.GetStore("x");
        factory.GetStore("x");

        Assert.Equal(1, source.Asked);
    }

    [Fact]
    public void Dispose_DisposesTheStoresItCreated()
    {
        var provider = new TrackingProvider();
        var factory = new DefaultObjectStoreFactory([provider], Declare(("a", "tracking")));

        factory.GetStore("a");
        factory.Dispose();

        Assert.Single(provider.Created);
        Assert.True(provider.Created[0].Disposed);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndLaterLookupsThrow()
    {
        var factory = new DefaultObjectStoreFactory([new MemoryObjectStoreProvider()], Declare(("a", "memory")));

        factory.Dispose();
        factory.Dispose();

        Assert.Throws<ObjectDisposedException>(() => factory.GetStore("a"));
    }

    [Fact]
    public void Constructor_NullProviders_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultObjectStoreFactory(null!));
    }

    [Fact]
    public void Registry_DuplicateNames_AreRefusedWithoutRegardToCase()
    {
        Assert.Throws<ArgumentException>(() => new ObjectStoreProviderRegistry([new MemoryObjectStoreProvider(), new NamedProvider("MEMORY")]));
    }

    [Fact]
    public void Registry_BlankName_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ObjectStoreProviderRegistry([new NamedProvider(" ")]));
    }

    [Fact]
    public void Registry_Find_IsCaseInsensitive()
    {
        var registry = new ObjectStoreProviderRegistry([new MemoryObjectStoreProvider()]);

        Assert.NotNull(registry.Find("MeMoRy"));
        Assert.Null(registry.Find("s3"));
        Assert.Equal(["memory"], registry.GetNames());
    }

    [Fact]
    public async Task MemoryProvider_KeepsDataAcrossRebuilds()
    {
        var provider = new MemoryObjectStoreProvider();
        var definition = new ObjectStoreDefinition { Name = "a", Provider = "memory" };

        await provider.Create(definition).PutAsync("k", new MemoryStream([1, 2, 3]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await provider.Create(definition).ExistsAsync("k", cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(await provider.GetStore("a")!.ExistsAsync("k", cancellationToken: TestContext.Current.CancellationToken));
    }
}
