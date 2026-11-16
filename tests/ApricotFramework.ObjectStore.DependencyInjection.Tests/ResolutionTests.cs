using ApricotFramework.ObjectStore.DependencyInjection.Extensions;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Options;
using Microsoft.Extensions.DependencyInjection;

namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

public class ResolutionTests
{
    private static ServiceProvider Provider(CapturingProvider capture, params string[] names)
    {
        return Build.Provider(s => s
            .AddObjectStores(o =>
            {
                o.DefaultStore = names[0];

                foreach (var name in names)
                {
                    o.Stores[name] = new ObjectStoreEntry { Provider = "capture" };
                }
            })
            .AddObjectStoreProvider(capture));
    }

    [Fact]
    public void Factory_IsOneSingleton()
    {
        using var provider = Provider(new CapturingProvider(), "a");

        Assert.Same(provider.GetRequiredService<IObjectStoreFactory>(), provider.GetRequiredService<IObjectStoreFactory>());
    }

    [Fact]
    public void Unkeyed_IsTheDefaultStore()
    {
        using var provider = Provider(new CapturingProvider(), "second", "first");

        Assert.Equal("second", provider.GetRequiredService<IObjectStore>().Name);
    }

    [Fact]
    public void Keyed_ResolvesAnyConfiguredName()
    {
        using var provider = Provider(new CapturingProvider(), "a", "b");

        Assert.Equal("a", provider.GetRequiredKeyedService<IObjectStore>("a").Name);
        Assert.Equal("b", provider.GetRequiredKeyedService<IObjectStore>("b").Name);
    }

    [Fact]
    public void Keyed_UnknownName_ThrowsStoreNotFound()
    {
        using var provider = Provider(new CapturingProvider(), "a");

        var e = Assert.Throws<ObjectStoreException>(() => provider.GetRequiredKeyedService<IObjectStore>("nope"));

        Assert.Equal(ObjectStoreErrorCode.StoreNotFound, e.ErrorCode);
    }

    [Fact]
    public void EveryRoute_ReachesTheSameUnderlyingStore()
    {
        var capture = new CapturingProvider();

        using var provider = Provider(capture, "a");

        provider.GetRequiredService<IObjectStore>();
        provider.GetRequiredKeyedService<IObjectStore>("a");
        provider.GetRequiredService<IObjectStoreFactory>().GetStore("a");
        provider.GetRequiredService<IObjectStoreFactory>().GetDefaultStore();

        // one client behind however many ways of asking
        Assert.Single(capture.Created);
    }

    [Fact]
    public void ResolvingAStoreInAScope_AndEndingTheScope_DoesNotDisposeTheSharedStore()
    {
        var capture = new CapturingProvider();

        using var provider = Provider(capture, "a");

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IObjectStore>();
            scope.ServiceProvider.GetRequiredKeyedService<IObjectStore>("a");
        }

        Assert.False(capture.Created[0].Disposed);

        provider.GetRequiredService<IObjectStore>();
    }

    [Fact]
    public void DisposingTheContainer_DisposesTheStoresThatWereCreated()
    {
        var capture = new CapturingProvider();
        var provider = Provider(capture, "a");

        provider.GetRequiredService<IObjectStore>();
        provider.Dispose();

        Assert.True(capture.Created[0].Disposed);
    }

    [Fact]
    public void Sources_AreAskedBeforeConfiguration()
    {
        var capture = new CapturingProvider();
        var source = new FixedSource(new ObjectStoreDefinition { Name = "a", Provider = "capture", Settings = new ObjectStoreSettings(new Dictionary<string, string?> { ["Bucket"] = "from-source" }) });

        using var provider = Build.Provider(s => s
            .AddObjectStores(o =>
            {
                o.DefaultStore = "a";
                o.Stores["a"] = new ObjectStoreEntry { Provider = "capture", Settings = { ["Bucket"] = "from-config" } };
            })
            .AddObjectStoreProvider(capture)
            .AddObjectStoreDefinitionSource(source));

        provider.GetRequiredService<IObjectStore>();

        Assert.Equal("from-source", capture.Definitions[0].Settings.GetString("Bucket"));
    }

    [Fact]
    public void SourceTypes_AreRegisteredOnce()
    {
        var services = new ServiceCollection();

        services.AddObjectStoreDefinitionSource<EmptyDefinitionSource>().AddObjectStoreDefinitionSource<EmptyDefinitionSource>();

        Assert.Single(services, d => d.ServiceType == typeof(IObjectStoreDefinitionSource));
    }

    [Fact]
    public void ProviderTypes_AreRegisteredOnce()
    {
        var services = new ServiceCollection();

        services.AddObjectStoreProvider<MemoryObjectStoreProvider>().AddObjectStoreProvider<MemoryObjectStoreProvider>();

        Assert.Single(services, d => d.ServiceType == typeof(IObjectStoreProvider));
    }

    [Fact]
    public async Task MemoryProvider_IsOneInstanceUnderBothTypes_SoTestsCanReadWhatWasWritten()
    {
        using var provider = Build.Provider(s => s
            .AddMemoryObjectStoreProvider()
            .AddMemoryObjectStoreProvider()
            .AddObjectStores(o =>
            {
                o.DefaultStore = "mem";
                o.Stores["mem"] = new ObjectStoreEntry { Provider = "memory" };
            }));

        Assert.Same(provider.GetRequiredService<MemoryObjectStoreProvider>(), provider.GetServices<IObjectStoreProvider>().Single());

        await provider.GetRequiredService<IObjectStore>().PutAsync("k", new MemoryStream([1, 2]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(await provider.GetRequiredService<MemoryObjectStoreProvider>().GetStore("mem")!.ExistsAsync("k", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void NoStoreNamedDefault_UnkeyedResolutionFails_ButNamedOnesStillWork()
    {
        // startup validation refuses this when the stores are in configuration, so it can only be reached
        // when a source supplies them and validation has to leave the question open
        using var provider = Build.Provider(s => s
            .AddObjectStores(_ => { })
            .AddObjectStoreProvider(new CapturingProvider())
            .AddObjectStoreDefinitionSource(new FixedSource(new ObjectStoreDefinition { Name = "a", Provider = "capture" })));

        var e = Assert.Throws<ObjectStoreException>(() => provider.GetRequiredService<IObjectStore>());

        Assert.Equal(ObjectStoreErrorCode.StoreNotFound, e.ErrorCode);
        Assert.Equal("a", provider.GetRequiredKeyedService<IObjectStore>("a").Name);
    }
}
