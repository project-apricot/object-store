using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApricotFramework.ObjectStore.S3.Extensions;

/// <summary>
/// Registers this package's provider on an <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// A provider needs no configuration of its own: everything it reads arrives on the store definition,
/// so this call takes nothing and can be made before or after <c>AddObjectStores</c>.
/// </remarks>
public static class S3ObjectStoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds the S3 provider, which stores reach by setting <c>Provider</c> to
    /// <see cref="S3ObjectStoreProvider.ProviderName"/>.
    /// </summary>
    /// <param name="services">The services collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Registered as a singleton. Calling this twice is harmless; registering a second provider that
    /// answers to <c>s3</c> is not, and fails when the factory is first resolved. To substitute a
    /// subclass, register it instead of calling this.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services"/> is null.</exception>
    public static IServiceCollection AddS3ObjectStoreProvider(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IObjectStoreProvider, S3ObjectStoreProvider>());

        return services;
    }
}
