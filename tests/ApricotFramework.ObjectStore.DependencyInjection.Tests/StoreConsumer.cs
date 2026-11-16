using Microsoft.Extensions.DependencyInjection;

namespace ApricotFramework.ObjectStore.DependencyInjection.Tests;

/// <summary>
/// A service the way an application writes one: stores injected by constructor, and kept for its lifetime.
/// </summary>
/// <param name="uploads">The default store.</param>
/// <param name="avatars">The store configured as <c>avatars</c>.</param>
/// <param name="factory">The factory, for a store chosen at the time of use.</param>
internal sealed class StoreConsumer(IObjectStore uploads, [FromKeyedServices("avatars")] IObjectStore avatars, IObjectStoreFactory factory)
{
    /// <summary>
    /// Gets the default store, as it was when this service was built.
    /// </summary>
    public IObjectStore Uploads { get; } = uploads;

    /// <summary>
    /// Gets the keyed store, as it was when this service was built.
    /// </summary>
    public IObjectStore Avatars { get; } = avatars;

    /// <summary>
    /// Gets the factory.
    /// </summary>
    public IObjectStoreFactory Factory { get; } = factory;
}
