using ApricotFramework.ObjectStore.DependencyInjection.Extensions;
using ApricotFramework.ObjectStore.Options;
using Microsoft.Extensions.DependencyInjection;

namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

/// <summary>
/// Stores are meant to be injected into long-lived services directly, without going through the factory first.
/// </summary>
public class SingletonUsageTests
{
    private static ServiceProvider Provider(CapturingProvider capture)
    {
        return Build.Provider(services =>
        {
            services.AddObjectStores(options =>
            {
                options.DefaultStore = "uploads";
                options.Stores["uploads"] = new ObjectStoreEntry { Provider = "capture" };
                options.Stores["avatars"] = new ObjectStoreEntry { Provider = "capture" };
            });

            services.AddObjectStoreProvider(capture);
            services.AddSingleton<StoreConsumer>();
        });
    }

    [Fact]
    public void ASingleton_CanTakeTheDefaultAndAKeyedStoreByConstructor_WithScopeValidationOn()
    {
        using var provider = Provider(new CapturingProvider());

        var consumer = provider.GetRequiredService<StoreConsumer>();

        Assert.Equal("uploads", consumer.Uploads.Name);
        Assert.Equal("avatars", consumer.Avatars.Name);
    }

    [Fact]
    public void ASingleton_AndAScopedService_ShareTheSameUnderlyingStore()
    {
        var capture = new CapturingProvider();

        using var provider = Provider(capture);

        provider.GetRequiredService<StoreConsumer>();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IObjectStore>();

        // one store per name however many ways of asking: the default and the keyed one, and nothing more
        Assert.Equal(2, capture.Created.Count);
    }

    [Fact]
    public async Task ASingletonKeepsWorking_AcrossManyScopesEnding()
    {
        var capture = new CapturingProvider();

        using var provider = Provider(capture);

        var consumer = provider.GetRequiredService<StoreConsumer>();

        for (var i = 0; i < 3; i++)
        {
            using var scope = provider.CreateScope();

            scope.ServiceProvider.GetRequiredService<IObjectStore>();
        }

        await consumer.Uploads.PutAsync("k", new MemoryStream([1]), cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(capture.Created, store => Assert.False(store.Disposed));
    }
}
